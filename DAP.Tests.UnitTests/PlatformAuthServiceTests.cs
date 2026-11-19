using System.Security.Claims;
using DAP.Core.Domain.Entities;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Presentation.BlazorWeb.Security.Auditing;
using DAP.Presentation.BlazorWeb.Security.Directory;
using DAP.Presentation.BlazorWeb.Security.External;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 本地账号登录校验的单元测试。
///
/// 这里钉住的规则出错都是安全事故：先判状态再验凭据会变成账号探针；
/// 锁定计数不清零会让解锁即再锁；改密不换安全戳会让已签发会话活到绝对过期。
/// </summary>
public sealed class PlatformAuthServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);
    private const string Password = "Initial@123";

    /// <summary>
    /// 可手动推进时间的 TimeProvider：锁定到期、会话校验等行为依赖时间流逝。
    /// </summary>
    private sealed class MutableTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    /// <summary>
    /// 目录替身：登录链路用。认证结果固定返回，供各用例自定义成功/失败。
    /// </summary>
    private sealed class StubLdapDirectory(LdapAuthenticationResult result) : ILdapDirectory
    {
        public Task<LdapAuthenticationResult> AuthenticateAsync(
            string? userName, string? password, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<LdapDirectoryUser>> ListUsersAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LdapDirectoryUser>>([]);
        }
    }

    private static UserSessionValidator CreateValidator(DataAcquisitionPlatformDbContext db)
    {
        return new UserSessionValidator(db, new MemoryCache(new MemoryCacheOptions()));
    }

    private static PlatformAuthService CreateService(
        DataAcquisitionPlatformDbContext db,
        PlatformAuthOptions options,
        TimeProvider timeProvider,
        ILdapDirectory? ldap = null,
        UserSessionValidator? validator = null)
    {
        return new PlatformAuthService(
            db,
            Options.Create(options),
            timeProvider,
            ldap ?? new StubLdapDirectory(LdapAuthenticationResult.Failed("账号或密码不正确。")),
            new DirectoryAccountService(
                db,
                new DepartmentCodeResolver(db),
                Options.Create(options),
                timeProvider,
                NullLogger<DirectoryAccountService>.Instance),
            validator ?? CreateValidator(db),
            NullLogger<PlatformAuthService>.Instance);
    }

    private static PlatformAuthOptions DefaultOptions()
    {
        return new PlatformAuthOptions
        {
            Lockout = { MaxFailedAttempts = 3, LockoutMinutes = 15 }
        };
    }

    // ---------- 种子辅助：所有非空字段显式赋值（SQLite 库已剥离 PG 默认值） ----------

    private static Permission SeedPermission(DataAcquisitionPlatformDbContext db, string code)
    {
        Permission permission = new()
        {
            Id = Guid.NewGuid(), Code = code, Name = code, SortOrder = 0,
            CreatedAt = Now, UpdatedAt = Now
        };
        db.Permissions.Add(permission);
        return permission;
    }

    private static Role SeedRole(
        DataAcquisitionPlatformDbContext db, string code, int pageLevel, bool enabled,
        params Permission[] permissions)
    {
        Role role = new()
        {
            Id = Guid.NewGuid(), Code = code, Name = code, PageLevel = pageLevel,
            IsBuiltin = true, IsEnabled = enabled, SortOrder = 10,
            CreatedAt = Now, UpdatedAt = Now
        };

        foreach (Permission permission in permissions)
            role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, Permission = permission });

        db.Roles.Add(role);
        return role;
    }

    private static AppUser SeedUser(
        string userName,
        string? password = Password,
        Guid? departmentId = null,
        bool enabled = true)
    {
        return new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            DisplayName = userName,
            DepartmentId = departmentId,
            IsEnabled = enabled,
            MustChangePassword = false,
            // password 为 null 表示外部账号（无本地密码）；否则按明文哈希落库。
            PasswordHash = password is null ? null : PasswordHasher.Hash(password),
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }

    private static void AddUserRole(DataAcquisitionPlatformDbContext db, AppUser user, Role role)
    {
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, Role = role, User = user });
    }

    private static Department SeedDepartment(DataAcquisitionPlatformDbContext db, string code)
    {
        Department department = new()
        {
            Id = Guid.NewGuid(), Code = code, Name = code, ExternalCode = null,
            IsEnabled = true, CreatedAt = Now, UpdatedAt = Now
        };
        db.Departments.Add(department);
        return department;
    }

    private static BusinessDomain SeedDomain(
        DataAcquisitionPlatformDbContext db, string code, bool enabled = true,
        bool isSystem = false, int sortOrder = 10)
    {
        BusinessDomain domain = new()
        {
            Id = Guid.NewGuid(), Code = code, Name = code, SortOrder = sortOrder,
            IsSystem = isSystem, IsEnabled = enabled, VisibleWhenEmpty = false,
            CreatedAt = Now, UpdatedAt = Now
        };
        db.BusinessDomains.Add(domain);
        return domain;
    }

    private static DomainPage SeedPage(
        DataAcquisitionPlatformDbContext db, BusinessDomain domain, string route,
        bool enabled = true, bool orphaned = false, int sortOrder = 10)
    {
        DomainPage page = new()
        {
            Id = Guid.NewGuid(), PageKey = $"key-{route}", DomainId = domain.Id, Route = route,
            Title = route, SortOrder = sortOrder, IsEnabled = enabled, IsOrphaned = orphaned,
            Source = AuthorizationCatalog.PageSourceCode, CreatedAt = Now, UpdatedAt = Now
        };
        db.DomainPages.Add(page);
        return page;
    }

    private static void SeedGrant(DataAcquisitionPlatformDbContext db, Department department, BusinessDomain domain)
    {
        db.DepartmentDomainGrants.Add(new DepartmentDomainGrant
        {
            DepartmentId = department.Id, DomainId = domain.Id, GrantedAt = Now, GrantedBy = null
        });
    }

    // ---------- SignInAsync：输入校验与账号枚举防护 ----------

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "something")]
    [InlineData("admin", "")]
    public async Task SignInAsync_Empty_Input_Should_Be_Rejected(string? userName, string? password)
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));

        AuthSignInResult result = await service.SignInAsync(userName, password);

        Assert.False(result.Succeeded);
        Assert.Equal("请输入账号与密码。", result.ErrorMessage);
    }

    [Fact]
    public async Task SignInAsync_Unknown_Account_Should_Return_Generic_Failure()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));

        AuthSignInResult result = await service.SignInAsync("ghost", "whatever1");

        // 对外必须是同一句话：不能让未认证的人区分「账号不存在」与其他失败。
        Assert.False(result.Succeeded);
        Assert.Equal("账号或密码不正确。", result.ErrorMessage);
    }

    [Fact]
    public async Task SignInAsync_Directory_Account_With_Ldap_Disabled_Should_Return_Generic_Failure()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.AppUsers.Add(SeedUser("zhang3", password: null));
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));

        AuthSignInResult result = await service.SignInAsync("zhang3", "whatever1");

        Assert.False(result.Succeeded);
        Assert.Equal("账号或密码不正确。", result.ErrorMessage);
    }

    [Fact]
    public async Task SignInAsync_Valid_Credentials_Should_Succeed_And_Reset_Counters()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("admin", password: Password);
        user.AccessFailedCount = 2;
        user.LastLoginAt = null;
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        Guid originalStamp = user.SecurityStamp;

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthSignInResult result = await service.SignInAsync("  ADMIN  ", Password);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotNull(result.User);
        Assert.Equal(user.Id, result.User!.Id);
        Assert.Equal("admin", result.User.UserName);
        Assert.Equal(originalStamp.ToString(), result.SecurityStamp);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser? reloaded = await verify.AppUsers.SingleAsync(item => item.UserName == "admin");
        Assert.Equal(0, reloaded.AccessFailedCount);
        Assert.Null(reloaded.LockoutEndAt);
        Assert.Equal(Now, reloaded.LastLoginAt);
    }

    // ---------- SignInAsync：锁定策略 ----------

    [Fact]
    public async Task SignInAsync_Wrong_Password_Should_Increment_Failed_Count()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.AppUsers.Add(SeedUser("admin", password: Password));
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthSignInResult result = await service.SignInAsync("admin", "WrongPass1");

        Assert.False(result.Succeeded);
        Assert.Equal("账号或密码不正确。", result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Equal(1, (await verify.AppUsers.SingleAsync(item => item.UserName == "admin")).AccessFailedCount);
    }

    [Fact]
    public async Task SignInAsync_Repeated_Failures_Should_Lock_Account_And_Reset_Counter()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.AppUsers.Add(SeedUser("admin", password: Password));
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));

        AuthSignInResult? lockedResult = null;
        for (int attempt = 1; attempt <= 3; attempt++)
            lockedResult = await service.SignInAsync("admin", "WrongPass1");

        Assert.False(lockedResult!.Succeeded);
        Assert.Equal("密码连续错误 3 次，账号已锁定 15 分钟。", lockedResult.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser user = await verify.AppUsers.SingleAsync(item => item.UserName == "admin");
        Assert.Equal(Now.AddMinutes(15), user.LockoutEndAt);
        // 计数清零：锁定期满后重新给满次数，否则解锁即再锁。
        Assert.Equal(0, user.AccessFailedCount);
    }

    [Fact]
    public async Task SignInAsync_Locked_Account_Should_Report_Remaining_Minutes()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("admin", password: Password);
        user.LockoutEndAt = Now.AddMinutes(10);
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthSignInResult result = await service.SignInAsync("admin", Password);

        Assert.False(result.Succeeded);
        Assert.Equal("登录失败次数过多，请在 10 分钟后重试。", result.ErrorMessage);
    }

    [Fact]
    public async Task SignInAsync_Lockout_Expired_Should_Allow_Sign_In_Again()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("admin", password: Password);
        user.LockoutEndAt = Now.AddMinutes(15);
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        MutableTimeProvider time = new() { UtcNow = Now.AddMinutes(16) };
        PlatformAuthService service = CreateService(db, DefaultOptions(), time);
        AuthSignInResult result = await service.SignInAsync("admin", Password);

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Null((await verify.AppUsers.SingleAsync(item => item.UserName == "admin")).LockoutEndAt);
    }

    // ---------- SignInAsync：停用账号（先验凭据，再判状态） ----------

    [Fact]
    public async Task SignInAsync_Disabled_Account_With_Wrong_Password_Should_Report_Password_Error()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.AppUsers.Add(SeedUser("admin", password: Password, enabled: false));
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthSignInResult result = await service.SignInAsync("admin", "WrongPass1");

        // 先验凭据再判状态：输任意密码就能区分「停用」等于给了未认证的人一个账号探针。
        Assert.False(result.Succeeded);
        Assert.Equal("账号或密码不正确。", result.ErrorMessage);
    }

    [Fact]
    public async Task SignInAsync_Disabled_Account_With_Correct_Password_Should_Be_Rejected()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.AppUsers.Add(SeedUser("admin", password: Password, enabled: false));
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthSignInResult result = await service.SignInAsync("admin", Password);

        Assert.False(result.Succeeded);
        Assert.Equal("该账号已停用，请联系管理员。", result.ErrorMessage);
    }

    [Fact]
    public async Task SignInAsync_Should_Expose_MustChangePassword_Flag()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("admin", password: Password);
        user.MustChangePassword = true;
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthSignInResult result = await service.SignInAsync("admin", Password);

        Assert.True(result.Succeeded);
        Assert.True(result.User!.MustChangePassword);
    }

    // ---------- SignInAsync：域账号（LDAP）链路 ----------

    [Fact]
    public async Task SignInAsync_Ldap_Success_Should_Provision_Account_And_Return_Stamp()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        SeedRole(db, AuthorizationCatalog.ViewerRole, AuthorizationCatalog.ViewAccessLevel, true);
        await db.SaveChangesAsync();

        LdapDirectoryUser directoryUser = new("ext-1", "zhang3", "张三", "zhang3@example.com", null, null, true);
        PlatformAuthOptions options = DefaultOptions();
        options.Ldap.Enabled = true;
        PlatformAuthService service = CreateService(
            db, options, new FixedTimeProvider(Now),
            new StubLdapDirectory(LdapAuthenticationResult.Success(directoryUser)));

        AuthSignInResult result = await service.SignInAsync("zhang3", "domain-password1");

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal("zhang3", result.User!.UserName);
        Assert.False(result.User.MustChangePassword);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser? created = await verify.AppUsers
            .Include(item => item.ExternalIdentities)
            .Include(item => item.UserRoles)
            .ThenInclude(link => link.Role)
            .FirstOrDefaultAsync(item => item.UserName == "zhang3");

        Assert.NotNull(created);
        // 外部账号没有本地密码：password_hash 为空即表示「只能用目录登录」。
        Assert.Null(created!.PasswordHash);
        Assert.Equal(Now, created.LastLoginAt);
        Assert.Contains(created.UserRoles, link => link.Role!.Code == AuthorizationCatalog.ViewerRole);
        Assert.Contains(created.ExternalIdentities,
            identity => identity.Provider == DirectorySyncService.LdapProvider && identity.ExternalId == "ext-1");
        Assert.Equal(created.SecurityStamp.ToString(), result.SecurityStamp);
    }

    [Fact]
    public async Task SignInAsync_Ldap_Authentication_Failure_Should_Propagate_Message()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        PlatformAuthOptions options = DefaultOptions();
        options.Ldap.Enabled = true;
        PlatformAuthService service = CreateService(
            db, options, new FixedTimeProvider(Now),
            new StubLdapDirectory(LdapAuthenticationResult.Failed("目录账号或密码不正确。")));

        AuthSignInResult result = await service.SignInAsync("zhang3", "wrong");

        Assert.False(result.Succeeded);
        Assert.Equal("目录账号或密码不正确。", result.ErrorMessage);
    }

    [Fact]
    public async Task SignInAsync_Ldap_Provision_Name_Conflict_Should_Fail_With_Readable_Message()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        // 本地已存在同名账号但没有本地密码（无法本地登录、也未绑定目录）。
        db.AppUsers.Add(SeedUser("zhang3", password: null));
        await db.SaveChangesAsync();

        LdapDirectoryUser directoryUser = new("ext-9", "zhang3", "张三", null, null, null, true);
        PlatformAuthOptions options = DefaultOptions();
        options.Ldap.Enabled = true;
        PlatformAuthService service = CreateService(
            db, options, new FixedTimeProvider(Now),
            new StubLdapDirectory(LdapAuthenticationResult.Success(directoryUser)));

        AuthSignInResult result = await service.SignInAsync("zhang3", "domain-password1");

        // 并号可能把域身份挂到本地管理员身上，必须拒绝而不是静默建第二个账号。
        Assert.False(result.Succeeded);
        Assert.Contains("已被平台上的本地账号占用", result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Equal(1, await verify.AppUsers.CountAsync(item => item.UserName == "zhang3"));
    }

    [Fact]
    public async Task SignInAsync_Ldap_Disabled_Local_User_Should_Be_Rejected()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("zhang3", password: null, enabled: false);
        user.DisabledSource = UserDisableSources.Manual;
        db.AppUsers.Add(user);
        db.ExternalIdentities.Add(new ExternalIdentity
        {
            Id = Guid.NewGuid(), UserId = user.Id, Provider = DirectorySyncService.LdapProvider,
            ExternalId = "ext-1", CreatedAt = Now, UpdatedAt = Now, LastSyncedAt = Now
        });
        await db.SaveChangesAsync();

        LdapDirectoryUser directoryUser = new("ext-1", "zhang3", "张三", null, null, null, true);
        PlatformAuthOptions options = DefaultOptions();
        options.Ldap.Enabled = true;
        PlatformAuthService service = CreateService(
            db, options, new FixedTimeProvider(Now),
            new StubLdapDirectory(LdapAuthenticationResult.Success(directoryUser)));

        AuthSignInResult result = await service.SignInAsync("zhang3", "domain-password1");

        Assert.False(result.Succeeded);
        Assert.Equal("该账号已停用，请联系管理员。", result.ErrorMessage);
    }

    // ---------- 授权视图构建：只认「启用」的域/角色/账号 ----------

    [Fact]
    public async Task SignInAsync_Disabled_Role_Should_Not_Contribute_Roles_Or_Permissions()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Permission shiftManage = SeedPermission(db, AuthorizationCatalog.ShiftManagePermission);
        SeedRole(db, AuthorizationCatalog.ViewerRole, AuthorizationCatalog.ViewAccessLevel, true);
        SeedRole(db, AuthorizationCatalog.TechnicianRole, AuthorizationCatalog.ConfigureAccessLevel,
            false, shiftManage);
        AppUser user = SeedUser("admin", password: Password);
        db.AppUsers.Add(user);
        AddUserRole(db, user, db.Roles.Local.Single(item => item.Code == AuthorizationCatalog.ViewerRole));
        AddUserRole(db, user, db.Roles.Local.Single(item => item.Code == AuthorizationCatalog.TechnicianRole));
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthSignInResult result = await service.SignInAsync("admin", Password);

        Assert.True(result.Succeeded);
        // 停用角色不产生权限，但授权记录本身保留。
        Assert.Equal([AuthorizationCatalog.ViewerRole], result.User!.Roles);
        Assert.Empty(result.User.Permissions);
        Assert.Equal(AuthorizationCatalog.ViewAccessLevel, result.User.PageLevel);
    }

    [Fact]
    public async Task SignInAsync_Permissions_Should_Be_Union_Of_Enabled_Roles()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Permission shiftManage = SeedPermission(db, AuthorizationCatalog.ShiftManagePermission);
        Permission userManage = SeedPermission(db, AuthorizationCatalog.UserManagePermission);
        Role technician = SeedRole(db, AuthorizationCatalog.TechnicianRole,
            AuthorizationCatalog.ConfigureAccessLevel, true, shiftManage);
        Role departmentAdmin = SeedRole(db, AuthorizationCatalog.DepartmentAdminRole,
            AuthorizationCatalog.ConfigureAccessLevel, true, userManage, shiftManage);
        AppUser user = SeedUser("admin", password: Password);
        db.AppUsers.Add(user);
        AddUserRole(db, user, technician);
        AddUserRole(db, user, departmentAdmin);
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthSignInResult result = await service.SignInAsync("admin", Password);

        Assert.True(result.Succeeded);
        // 权限取并集、去重、按编码排序；页面等级取最大值。
        Assert.Equal(
            [AuthorizationCatalog.ShiftManagePermission, AuthorizationCatalog.UserManagePermission],
            result.User!.Permissions);
        Assert.Equal(AuthorizationCatalog.ConfigureAccessLevel, result.User.PageLevel);
    }

    [Fact]
    public async Task SignInAsync_Super_Admin_Should_See_All_Enabled_Domains()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        SeedRole(db, AuthorizationCatalog.SuperAdminRole, AuthorizationCatalog.SuperAdminPageLevel, true);
        SeedRole(db, AuthorizationCatalog.ViewerRole, AuthorizationCatalog.ViewAccessLevel, true);
        SeedDomain(db, AuthorizationCatalog.BanknotePaperDomain, sortOrder: 10);
        SeedDomain(db, AuthorizationCatalog.BanknotePrintingDomain, sortOrder: 20);
        SeedDomain(db, AuthorizationCatalog.EnergyDomain, sortOrder: 30);
        SeedDomain(db, AuthorizationCatalog.PlatformDomain, isSystem: true, sortOrder: 900);
        SeedDomain(db, "disabled-domain", enabled: false, sortOrder: 950);
        AppUser user = SeedUser("admin", password: Password);
        db.AppUsers.Add(user);
        AddUserRole(db, user, db.Roles.Local.Single(item => item.Code == AuthorizationCatalog.SuperAdminRole));
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthSignInResult result = await service.SignInAsync("admin", Password);

        Assert.True(result.Succeeded);
        Assert.True(result.User!.IsSuperAdmin);
        Assert.Equal(AuthorizationCatalog.SuperAdminPageLevel, result.User.PageLevel);
        // 超管可见全部启用域（含系统域），停用域不可见。
        Assert.Equal(
            [AuthorizationCatalog.BanknotePaperDomain, AuthorizationCatalog.BanknotePrintingDomain,
                AuthorizationCatalog.EnergyDomain, AuthorizationCatalog.PlatformDomain],
            result.User.Domains);
    }

    [Fact]
    public async Task SignInAsync_Granted_Domains_Should_Exclude_Disabled_And_System_Domains()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        SeedRole(db, AuthorizationCatalog.ViewerRole, AuthorizationCatalog.ViewAccessLevel, true);
        Department department = SeedDepartment(db, "D001");
        BusinessDomain energy = SeedDomain(db, AuthorizationCatalog.EnergyDomain, sortOrder: 30);
        BusinessDomain paper = SeedDomain(db, AuthorizationCatalog.BanknotePaperDomain, sortOrder: 10);
        SeedDomain(db, "disabled-domain", enabled: false);
        SeedDomain(db, AuthorizationCatalog.PlatformDomain, isSystem: true);
        SeedGrant(db, department, energy);
        SeedGrant(db, department, paper);
        SeedGrant(db, department, db.BusinessDomains.Local.Single(item => item.Code == "disabled-domain"));
        SeedGrant(db, department, db.BusinessDomains.Local.Single(item => item.Code == AuthorizationCatalog.PlatformDomain));

        AppUser user = SeedUser("member", password: Password, departmentId: department.Id);
        db.AppUsers.Add(user);
        AddUserRole(db, user, db.Roles.Local.Single(item => item.Code == AuthorizationCatalog.ViewerRole));
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthSignInResult result = await service.SignInAsync("member", Password);

        Assert.True(result.Succeeded);
        Assert.False(result.User!.IsSuperAdmin);
        // 停用域与系统域不进授权视图；顺序按域排序。
        Assert.Equal(
            [AuthorizationCatalog.BanknotePaperDomain, AuthorizationCatalog.EnergyDomain],
            result.User.Domains);
        Assert.Equal(department.Name, result.User.DepartmentName);
    }

    [Fact]
    public async Task SignInAsync_User_Without_Department_Should_Have_No_Domains()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        SeedRole(db, AuthorizationCatalog.ViewerRole, AuthorizationCatalog.ViewAccessLevel, true);
        AppUser user = SeedUser("member", password: Password, departmentId: null);
        db.AppUsers.Add(user);
        AddUserRole(db, user, db.Roles.Local.Single(item => item.Code == AuthorizationCatalog.ViewerRole));
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthSignInResult result = await service.SignInAsync("member", Password);

        Assert.True(result.Succeeded);
        // 域授权挂在部门上：没有部门的账号不可见任何业务域。
        Assert.Empty(result.User!.Domains);
        Assert.Null(result.User.DepartmentName);
    }

    // ---------- GetUserAsync ----------

    [Fact]
    public async Task GetUserAsync_Should_Return_Null_For_Missing_Or_Disabled_User()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        SeedRole(db, AuthorizationCatalog.ViewerRole, AuthorizationCatalog.ViewAccessLevel, true);
        AppUser enabled = SeedUser("member", password: Password);
        AppUser disabled = SeedUser("gone", password: Password, enabled: false);
        db.AppUsers.Add(enabled);
        db.AppUsers.Add(disabled);
        AddUserRole(db, enabled, db.Roles.Local.Single(item => item.Code == AuthorizationCatalog.ViewerRole));
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));

        Assert.Null(await service.GetUserAsync(Guid.NewGuid()));
        Assert.Null(await service.GetUserAsync(disabled.Id));
        AuthUserDto? dto = await service.GetUserAsync(enabled.Id);
        Assert.NotNull(dto);
        Assert.Equal([AuthorizationCatalog.ViewerRole], dto!.Roles);
    }

    // ---------- ResolveLandingPageAsync：四个分支分开处理 ----------

    [Fact]
    public async Task ResolveLandingPageAsync_Super_Admin_Should_Go_To_Platform_Overview()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));

        AuthUserDto user = new(Guid.NewGuid(), "admin", "超管", null, [], [], 99, [], false, true);
        Assert.Equal("/platform-overview", await service.ResolveLandingPageAsync(user));
    }

    [Fact]
    public async Task ResolveLandingPageAsync_No_Domains_Should_Go_To_No_Access()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));

        AuthUserDto user = new(Guid.NewGuid(), "member", "成员", null, [], [], 1, [], false, false);
        Assert.Equal("/no-access", await service.ResolveLandingPageAsync(user));
    }

    [Fact]
    public async Task ResolveLandingPageAsync_Should_Pick_First_Enabled_Page_By_Sort_Order()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        BusinessDomain paper = SeedDomain(db, AuthorizationCatalog.BanknotePaperDomain, sortOrder: 10);
        BusinessDomain energy = SeedDomain(db, AuthorizationCatalog.EnergyDomain, sortOrder: 30);
        SeedPage(db, energy, "/energy/second", sortOrder: 20);
        SeedPage(db, paper, "/paper/second", sortOrder: 20);
        SeedPage(db, paper, "/paper/disabled", enabled: false, sortOrder: 1);
        SeedPage(db, paper, "/paper/orphaned", orphaned: true, sortOrder: 1);
        SeedPage(db, paper, "/paper/first", sortOrder: 10);
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthUserDto user = new(
            Guid.NewGuid(), "member", "成员", null, [],
            [AuthorizationCatalog.EnergyDomain, AuthorizationCatalog.BanknotePaperDomain], 1, [], false, false);

        // 先按域排序再按页面排序；停用页与已下线页不参与。
        Assert.Equal("/paper/first", await service.ResolveLandingPageAsync(user));
    }

    [Fact]
    public async Task ResolveLandingPageAsync_Domain_Without_Pages_Should_Go_To_Coming_Soon()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        SeedDomain(db, AuthorizationCatalog.EnergyDomain);
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthUserDto user = new(
            Guid.NewGuid(), "member", "成员", null, [],
            [AuthorizationCatalog.EnergyDomain], 1, [], false, false);

        // 已授权但域内没有页面：内容未就绪，不是无权限。
        Assert.Equal("/coming-soon", await service.ResolveLandingPageAsync(user));
    }

    [Fact]
    public async Task ResolveLandingPageAsync_Disabled_Domain_Should_Go_To_Coming_Soon()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        BusinessDomain domain = SeedDomain(db, AuthorizationCatalog.EnergyDomain, enabled: false);
        SeedPage(db, domain, "/energy/dashboard");
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthUserDto user = new(
            Guid.NewGuid(), "member", "成员", null, [],
            [AuthorizationCatalog.EnergyDomain], 1, [], false, false);

        // 授权记录保留但域停用：无可见页面，与「域内没有页面」同分支（记录现状行为）。
        Assert.Equal("/coming-soon", await service.ResolveLandingPageAsync(user));
    }

    // ---------- ChangePasswordAsync ----------

    [Fact]
    public async Task ChangePasswordAsync_Wrong_Current_Password_Should_Be_Rejected()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("admin", password: Password);
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthOperationResultDto result = await service.ChangePasswordAsync(user.Id, "WrongPass1", "NewPass@123");

        Assert.False(result.Succeeded);
        Assert.Equal("当前密码不正确。", result.ErrorMessage);
    }

    [Fact]
    public async Task ChangePasswordAsync_Same_Password_Should_Be_Rejected()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("admin", password: Password);
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthOperationResultDto result = await service.ChangePasswordAsync(user.Id, Password, Password);

        Assert.False(result.Succeeded);
        Assert.Equal("新密码不能与当前密码相同。", result.ErrorMessage);
    }

    [Fact]
    public async Task ChangePasswordAsync_Weak_New_Password_Should_Be_Rejected()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("admin", password: Password);
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthOperationResultDto result = await service.ChangePasswordAsync(user.Id, Password, "weak");

        Assert.False(result.Succeeded);
        Assert.Equal("密码长度至少 8 位。", result.ErrorMessage);
    }

    [Fact]
    public async Task ChangePasswordAsync_Directory_Account_Should_Be_Rejected()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("zhang3", password: null);
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthOperationResultDto result = await service.ChangePasswordAsync(user.Id, "anything", "NewPass@123");

        Assert.False(result.Succeeded);
        Assert.Equal("域账号的密码由目录统一管理，请通过域账号修改。", result.ErrorMessage);
    }

    [Fact]
    public async Task ChangePasswordAsync_Disabled_Account_Should_Be_Rejected()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("admin", password: Password, enabled: false);
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));
        AuthOperationResultDto result = await service.ChangePasswordAsync(user.Id, Password, "NewPass@123");

        Assert.False(result.Succeeded);
        Assert.Equal("账号不存在或已停用。", result.ErrorMessage);
    }

    [Fact]
    public async Task ChangePasswordAsync_Success_Should_Renew_Stamp_And_Invalidate_Old_Sessions()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("admin", password: Password);
        user.MustChangePassword = true;
        user.AccessFailedCount = 2;
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        string oldStamp = user.SecurityStamp.ToString();

        UserSessionValidator validator = CreateValidator(db);
        // 改密前：老会话有效。
        Assert.True(await validator.IsSessionValidAsync(user.Id, oldStamp));

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now), validator: validator);
        AuthOperationResultDto result = await service.ChangePasswordAsync(user.Id, Password, "NewPass@123");

        Assert.True(result.Succeeded, result.ErrorMessage);

        // 改密等于「我怀疑旧密码可能外泄」：旧戳必须立即失效（缓存已同步清除）。
        Assert.False(await validator.IsSessionValidAsync(user.Id, oldStamp));

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser reloaded = await verify.AppUsers.SingleAsync(item => item.Id == user.Id);
        Assert.NotEqual(oldStamp, reloaded.SecurityStamp.ToString());
        Assert.True(PasswordHasher.Verify("NewPass@123", reloaded.PasswordHash!));
        Assert.False(PasswordHasher.Verify(Password, reloaded.PasswordHash!));
        Assert.False(reloaded.MustChangePassword);
        Assert.Equal(0, reloaded.AccessFailedCount);
        Assert.Null(reloaded.LockoutEndAt);
        Assert.True(await validator.IsSessionValidAsync(user.Id, reloaded.SecurityStamp.ToString()));
    }

    // ---------- GetSecurityStampAsync ----------

    [Fact]
    public async Task GetSecurityStampAsync_Should_Return_Current_Stamp_Or_Null()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("admin", password: Password);
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        PlatformAuthService service = CreateService(db, DefaultOptions(), new FixedTimeProvider(Now));

        Assert.Equal(user.SecurityStamp.ToString(), await service.GetSecurityStampAsync(user.Id));
        Assert.Null(await service.GetSecurityStampAsync(Guid.NewGuid()));
    }

    // ---------- UserSessionValidator：会话有效性 ----------

    [Fact]
    public async Task Session_Validation_Should_Fail_Closed()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser enabled = SeedUser("member", password: Password);
        AppUser disabled = SeedUser("gone", password: Password, enabled: false);
        db.AppUsers.Add(enabled);
        db.AppUsers.Add(disabled);
        await db.SaveChangesAsync();
        string stamp = enabled.SecurityStamp.ToString();

        UserSessionValidator validator = CreateValidator(db);

        Assert.True(await validator.IsSessionValidAsync(enabled.Id, stamp));
        // 戳不匹配、空戳、停用账号、账号不存在：全部拒绝。
        Assert.False(await validator.IsSessionValidAsync(enabled.Id, Guid.NewGuid().ToString()));
        Assert.False(await validator.IsSessionValidAsync(enabled.Id, null));
        Assert.False(await validator.IsSessionValidAsync(enabled.Id, ""));
        Assert.False(await validator.IsSessionValidAsync(disabled.Id, disabled.SecurityStamp.ToString()));
        Assert.False(await validator.IsSessionValidAsync(Guid.NewGuid(), "any"));
    }

    [Fact]
    public async Task Session_Validation_Should_Invalidate_Session_After_Disable()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("member", password: Password);
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        string stamp = user.SecurityStamp.ToString();

        UserSessionValidator validator = CreateValidator(db);
        Assert.True(await validator.IsSessionValidAsync(user.Id, stamp));

        // 30 秒缓存窗口内停用：RenewSecurityStamp 主动清缓存，判定立即生效。
        validator.RenewSecurityStamp(user);
        user.IsEnabled = false;
        await db.SaveChangesAsync();

        Assert.False(await validator.IsSessionValidAsync(user.Id, stamp));
        Assert.False(await validator.IsSessionValidAsync(user.Id, user.SecurityStamp.ToString()));
    }

    // ---------- 静态规则 ----------

    [Theory]
    [InlineData(null, "请输入新密码。")]
    [InlineData("", "请输入新密码。")]
    [InlineData("   ", "请输入新密码。")]
    [InlineData("ab1", "密码长度至少 8 位。")]
    [InlineData("1234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890", "密码长度不能超过 128 位。")]
    [InlineData("password", "密码需同时包含字母与数字。")]
    [InlineData("12345678", "密码需同时包含字母与数字。")]
    [InlineData("admin1234", "密码不能与账号相同。")]
    [InlineData("ADMIN1234", "密码不能与账号相同。")]
    [InlineData("Adm1n@2026", null)]
    public void ValidateNewPassword_Should_Enforce_Strength_Rules(string? password, string? expected)
    {
        Assert.Equal(expected, PlatformAuthService.ValidateNewPassword("admin1234", password));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  Admin  ", "admin")]
    [InlineData("Zhang3", "zhang3")]
    public void NormalizeUserName_Should_Trim_And_Lowercase(string? input, string expected)
    {
        Assert.Equal(expected, PlatformAuthService.NormalizeUserName(input));
    }
}
