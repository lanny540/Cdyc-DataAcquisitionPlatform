using DAP.Core.Domain.Entities;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Catalog;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Presentation.BlazorWeb.Security.Directory;
using DAP.Presentation.BlazorWeb.Security.External;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 域账号登录时建号与字段刷新的单元测试。
///
/// 与定时同步共用同一份构造逻辑（DirectoryUserFactory / DirectorySyncPlanner）：
/// 两条路径若各写一套字段映射，迟早出现「同步改的姓名被登录改回去了」。
/// </summary>
public sealed class DirectoryAccountServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);
    private const string Password = "Initial@123";

    private static DirectoryAccountService CreateService(
        DataAcquisitionPlatformDbContext db, PlatformAuthOptions? options = null)
    {
        options ??= new PlatformAuthOptions
        {
            Ldap = { Enabled = true, DefaultRoleCode = AuthorizationCatalog.ViewerRole }
        };

        return new DirectoryAccountService(
            db,
            new DepartmentCodeResolver(db),
            Options.Create(options),
            new FixedTimeProvider(Now),
            NullLogger<DirectoryAccountService>.Instance);
    }

    private static Role SeedRole(string code, bool enabled = true, int pageLevel = 1)
    {
        return new Role
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            PageLevel = pageLevel,
            IsBuiltin = true,
            IsEnabled = enabled,
            SortOrder = 10,
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }

    private static AppUser SeedUser(string userName, string? password = Password, Guid? departmentId = null)
    {
        return new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            DisplayName = userName,
            DepartmentId = departmentId,
            IsEnabled = true,
            MustChangePassword = false,
            PasswordHash = password is null ? null : PasswordHasher.Hash(password),
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }

    private static ExternalIdentity SeedIdentity(Guid userId, string externalId)
    {
        return new ExternalIdentity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = DirectorySyncService.LdapProvider,
            ExternalId = externalId,
            EmployeeNo = null,
            LastSyncedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }

    private static LdapDirectoryUser Record(
        string externalId = "ext-1",
        string userName = "zhang3",
        string displayName = "张三",
        string? email = "zhang3@example.com",
        string? departmentCode = null,
        bool enabled = true,
        string? employeeNo = "E1001")
    {
        return new LdapDirectoryUser(externalId, userName, displayName, email, departmentCode, employeeNo, enabled);
    }

    [Fact]
    public async Task EnsureUserAsync_New_Record_Should_Create_User_With_Default_Role()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Department workshop = new()
        {
            Id = Guid.NewGuid(),
            Code = "D001",
            Name = "抄纸一车间",
            ExternalCode = "PAPER-1",
            IsEnabled = true,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.Departments.Add(workshop);
        db.Roles.Add(SeedRole(AuthorizationCatalog.ViewerRole));
        await db.SaveChangesAsync();

        DirectoryAccountService service = CreateService(db);
        DirectoryProvisionResult result = await service.EnsureUserAsync(
            Record(departmentCode: "PAPER-1"));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotNull(result.User);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser created = await verify.AppUsers
            .Include(user => user.ExternalIdentities)
            .Include(user => user.UserRoles)
            .ThenInclude(link => link.Role)
            .SingleAsync(user => user.UserName == "zhang3");

        // 外部账号不设本地密码：password_hash 为空即表示「只能用目录登录」。
        Assert.Null(created.PasswordHash);
        Assert.Equal("张三", created.DisplayName);
        Assert.Equal("zhang3@example.com", created.Email);
        Assert.Equal(workshop.Id, created.DepartmentId);
        Assert.True(created.IsEnabled);
        Assert.Contains(created.UserRoles, link => link.Role!.Code == AuthorizationCatalog.ViewerRole);

        ExternalIdentity identity = created.ExternalIdentities
            .Single(item => item.Provider == DirectorySyncService.LdapProvider);
        Assert.Equal("ext-1", identity.ExternalId);
        Assert.Equal("E1001", identity.EmployeeNo);
        Assert.Equal(Now, identity.LastSyncedAt);
    }

    [Fact]
    public async Task EnsureUserAsync_Disabled_Record_Should_Create_Disabled_User()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.Roles.Add(SeedRole(AuthorizationCatalog.ViewerRole));
        await db.SaveChangesAsync();

        DirectoryAccountService service = CreateService(db);
        DirectoryProvisionResult result = await service.EnsureUserAsync(Record(enabled: false));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.False(result.User!.IsEnabled);
        Assert.Equal(UserDisableSources.Directory, result.User.DisabledSource);
    }

    [Fact]
    public async Task EnsureUserAsync_Existing_User_Should_Refresh_Fields_But_Not_Roles()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Department old = new()
        {
            Id = Guid.NewGuid(),
            Code = "D001",
            Name = "抄纸一车间",
            ExternalCode = "PAPER-1",
            IsEnabled = true,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        Department moved = new()
        {
            Id = Guid.NewGuid(),
            Code = "D002",
            Name = "能源车间",
            ExternalCode = "ENERGY-1",
            IsEnabled = true,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        Role technician = SeedRole(AuthorizationCatalog.TechnicianRole, pageLevel: 2);
        db.Departments.Add(old);
        db.Departments.Add(moved);
        db.Roles.Add(technician);

        AppUser user = SeedUser("zhang3", departmentId: old.Id);
        user.DisplayName = "张三";
        user.Email = null;
        db.AppUsers.Add(user);
        db.ExternalIdentities.Add(SeedIdentity(user.Id, "ext-1"));
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = technician.Id });
        await db.SaveChangesAsync();

        DirectoryAccountService service = CreateService(db);
        DirectoryProvisionResult result = await service.EnsureUserAsync(
            Record(displayName: "张三丰", email: "zhangsanfeng@example.com", departmentCode: "ENERGY-1"));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser updated = await verify.AppUsers
            .Include(item => item.UserRoles)
            .ThenInclude(link => link.Role)
            .SingleAsync(item => item.Id == user.Id);

        Assert.Equal("张三丰", updated.DisplayName);
        Assert.Equal("zhangsanfeng@example.com", updated.Email);
        Assert.Equal(moved.Id, updated.DepartmentId);
        Assert.Equal(Now, updated.UpdatedAt);
        // 角色只在建号时给默认值，之后不再由目录决定。
        Assert.Equal([AuthorizationCatalog.TechnicianRole],
            updated.UserRoles.Select(link => link.Role!.Code).ToList());
    }

    [Fact]
    public async Task EnsureUserAsync_Blank_Directory_Fields_Should_Not_Clear_Local_Values()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("zhang3");
        user.DisplayName = "张三";
        user.Email = "zhang3@example.com";
        db.AppUsers.Add(user);
        db.ExternalIdentities.Add(SeedIdentity(user.Id, "ext-1"));
        await db.SaveChangesAsync();

        // 目录里少填一个字段不该让平台上的姓名变成空白。
        DirectoryAccountService service = CreateService(db);
        DirectoryProvisionResult result = await service.EnsureUserAsync(
            Record(displayName: "  ", email: null));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser kept = await verify.AppUsers.SingleAsync(item => item.Id == user.Id);
        Assert.Equal("张三", kept.DisplayName);
        Assert.Equal("zhang3@example.com", kept.Email);
    }

    [Fact]
    public async Task EnsureUserAsync_Name_Occupied_By_Local_Account_Should_Refuse_To_Create()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.AppUsers.Add(SeedUser("zhang3"));
        await db.SaveChangesAsync();

        DirectoryAccountService service = CreateService(db);
        DirectoryProvisionResult result = await service.EnsureUserAsync(
            Record("ext-9", "zhang3"));

        // 并号可能把域身份挂到本地管理员身上，必须拒绝。
        Assert.False(result.Succeeded);
        Assert.Null(result.User);
        Assert.Contains("已被平台上的本地账号占用", result.ErrorMessage);
    }

    [Fact]
    public async Task EnsureUserAsync_Unavailable_Default_Role_Should_Create_Without_Roles()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.Roles.Add(SeedRole(AuthorizationCatalog.ViewerRole, false));
        await db.SaveChangesAsync();

        DirectoryAccountService service = CreateService(db);
        DirectoryProvisionResult result = await service.EnsureUserAsync(Record());

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser created = await verify.AppUsers
            .Include(user => user.UserRoles)
            .SingleAsync(user => user.UserName == "zhang3");
        Assert.Empty(created.UserRoles);
    }

    [Fact]
    public async Task ResolveDepartmentIdAsync_Should_Match_Registered_Departments_Only()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Department workshop = new()
        {
            Id = Guid.NewGuid(),
            Code = "D001",
            Name = "抄纸一车间",
            ExternalCode = "PAPER-1",
            IsEnabled = true,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.Departments.Add(workshop);
        await db.SaveChangesAsync();

        DirectoryAccountService service = CreateService(db);

        Assert.Equal(workshop.Id, await service.ResolveDepartmentIdAsync("PAPER-1"));
        // 匹配不到返回 null：宁可让人找管理员，也不要把人塞进一个猜出来的部门。
        Assert.Null(await service.ResolveDepartmentIdAsync("UNKNOWN"));
        Assert.Null(await service.ResolveDepartmentIdAsync(null));
        Assert.Null(await service.ResolveDepartmentIdAsync("   "));
    }
}
