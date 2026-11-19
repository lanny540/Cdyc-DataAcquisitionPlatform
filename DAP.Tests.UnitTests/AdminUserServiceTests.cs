using System.Security.Claims;
using DAP.Core.Domain.Entities;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Presentation.BlazorWeb.Security.Auditing;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAP.Tests.UnitTests;

/// <summary>
/// AdminUserService 的单元测试（docs/AUTH_DESIGN.md 6.4 / 9.3 / 9.5）。
///
/// 覆盖的核心安全规则：
/// - 超管管全部账号，部门管理员只管本部门（department_id 精确相等）；
/// - 部门管理员只能分配「普通用户 / 技术人员」，不能授予部门管理员及以上；
/// - 超管账号不可停用 / 不可改部门 / 不可被他人重置密码；
/// - 停用与重置密码换安全戳，使已签发会话立即失效；
/// - 越权拒绝同样写入审计（result = failed）。
/// </summary>
public sealed class AdminUserServiceTests
{
    private static readonly DateTimeOffset SeedTime = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset FixedNow = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 种子数据：两个部门、四个内置角色（全部启用）、五个权限点与角色权限关联、
    /// 超管 / 部门管理员A / 普通用户A / 普通用户B 四个账号。
    /// </summary>
    private sealed record SeedData(
        AppUser SuperAdmin,
        AppUser DeptAdminA,
        AppUser ViewerA,
        AppUser ViewerB,
        Department DeptA,
        Department DeptB,
        Guid SuperAdminRoleId,
        Guid DeptAdminRoleId,
        Guid TechnicianRoleId,
        Guid ViewerRoleId);

    private static SeedData Seed(SqliteTestDatabase database)
    {
        using DataAcquisitionPlatformDbContext db = database.CreateContext();

        Department deptA = new()
        {
            Id = Guid.NewGuid(), Code = "dept-a", Name = "一部", IsEnabled = true,
            CreatedAt = SeedTime, UpdatedAt = SeedTime
        };
        Department deptB = new()
        {
            Id = Guid.NewGuid(), Code = "dept-b", Name = "二部", IsEnabled = true,
            CreatedAt = SeedTime, UpdatedAt = SeedTime
        };
        db.Departments.AddRange(deptA, deptB);

        Dictionary<string, Guid> roleIds = [];
        foreach (BuiltInRole builtIn in AuthorizationCatalog.Roles)
        {
            Role role = new()
            {
                Id = Guid.NewGuid(), Code = builtIn.Code, Name = builtIn.Name,
                PageLevel = builtIn.PageLevel, IsBuiltin = builtIn.IsBuiltin,
                IsEnabled = true, SortOrder = builtIn.SortOrder,
                CreatedAt = SeedTime, UpdatedAt = SeedTime
            };
            roleIds[builtIn.Code] = role.Id;
            db.Roles.Add(role);
        }

        Dictionary<string, Guid> permissionIds = [];
        foreach (BuiltInPermission builtIn in AuthorizationCatalog.Permissions)
        {
            Permission permission = new()
            {
                Id = Guid.NewGuid(), Code = builtIn.Code, Name = builtIn.Name,
                Group = builtIn.Group, SortOrder = builtIn.SortOrder,
                CreatedAt = SeedTime, UpdatedAt = SeedTime
            };
            permissionIds[builtIn.Code] = permission.Id;
            db.Permissions.Add(permission);
        }

        foreach (BuiltInRole builtIn in AuthorizationCatalog.Roles)
        foreach (string code in builtIn.PermissionCodes)
            db.RolePermissions.Add(new RolePermission
            {
                RoleId = roleIds[builtIn.Code], PermissionId = permissionIds[code]
            });

        AppUser superAdmin = CreateUser("superadmin", "超级管理员", null);
        AppUser deptAdminA = CreateUser("dept-admin-a", "一部管理员", deptA.Id);
        AppUser viewerA = CreateUser("viewer-a", "一部用户", deptA.Id);
        AppUser viewerB = CreateUser("viewer-b", "二部用户", deptB.Id);

        db.AppUsers.AddRange(superAdmin, deptAdminA, viewerA, viewerB);

        db.UserRoles.AddRange(
            new UserRole { UserId = superAdmin.Id, RoleId = roleIds[AuthorizationCatalog.SuperAdminRole] },
            new UserRole { UserId = deptAdminA.Id, RoleId = roleIds[AuthorizationCatalog.DepartmentAdminRole] },
            new UserRole { UserId = viewerA.Id, RoleId = roleIds[AuthorizationCatalog.ViewerRole] },
            new UserRole { UserId = viewerB.Id, RoleId = roleIds[AuthorizationCatalog.ViewerRole] });

        db.SaveChanges();

        return new SeedData(
            superAdmin, deptAdminA, viewerA, viewerB, deptA, deptB,
            roleIds[AuthorizationCatalog.SuperAdminRole],
            roleIds[AuthorizationCatalog.DepartmentAdminRole],
            roleIds[AuthorizationCatalog.TechnicianRole],
            roleIds[AuthorizationCatalog.ViewerRole]);
    }

    private static AppUser CreateUser(string userName, string displayName, Guid? departmentId)
    {
        return new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            DisplayName = displayName,
            DepartmentId = departmentId,
            IsEnabled = true,
            MustChangePassword = false,
            PasswordHash = PasswordHasher.Hash("Passw0rd123"),
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime
        };
    }

    /// <summary>
    /// 从 claims 构造操作者视图，与生产 Cookie 会话的 claims 结构一致：
    /// 权限判定只认 claims，超管 claim 存在即短路。
    /// </summary>
    private static CurrentUser BuildActor(AppUser user, bool isSuperAdmin = false, params string[] permissions)
    {
        List<Claim> claims =
        [
            new(AuthorizationCatalog.PlatformClaimTypes.UserId, user.Id.ToString()),
            new(AuthorizationCatalog.PlatformClaimTypes.UserName, user.UserName),
            new(AuthorizationCatalog.PlatformClaimTypes.DisplayName, user.DisplayName)
        ];

        if (isSuperAdmin)
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.SuperAdmin, "true"));

        foreach (string permission in permissions)
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.Permission, permission));

        ClaimsIdentity identity = new(claims, "TestAuthentication");
        return CurrentUser.FromPrincipal(new ClaimsPrincipal(identity), "127.0.0.1", "unit-test-agent")!;
    }

    private static CurrentUser SuperAdminActor(AppUser user)
        => BuildActor(user, isSuperAdmin: true);

    private static CurrentUser DeptAdminActor(AppUser user)
        => BuildActor(user, permissions: AuthorizationCatalog.UserManagePermission);

    private static AdminUserService CreateService(
        DataAcquisitionPlatformDbContext db,
        CurrentUser? actor,
        TimeProvider time)
    {
        return new AdminUserService(
            db,
            new AuditLogWriter(db, time, NullLogger<AuditLogWriter>.Instance),
            new StubUserContext(actor),
            time,
            new UserSessionValidator(db, new MemoryCache(new MemoryCacheOptions())));
    }

    private static Task<AuthOperationResultDto> UpdateUserAsync(
        AdminUserService service, Guid userId, string displayName, bool isEnabled)
    {
        return service.UpdateUserAsync(userId, new AdminUserUpdateRequest(displayName, null, isEnabled));
    }

    /// <summary>固定返回同一个操作者的用户上下文替身。</summary>
    private sealed class StubUserContext(CurrentUser? user) : ICurrentUserContext
    {
        public Task<CurrentUser?> GetUserAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(user);
    }

    /// <summary>
    /// 从审计明细 JSON 中取 reason 字段。
    /// Detail 列按默认 JSON 编码存储，非 ASCII 字符会转成 \uXXXX，直接 Contains 中文会失配。
    /// </summary>
    private static string? DetailReason(string? detail)
    {
        if (detail is null) return null;

        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(detail);
        return document.RootElement.TryGetProperty("reason", out System.Text.Json.JsonElement reason)
            ? reason.GetString()
            : null;
    }

    // ------------------------------------------------------------------
    // GetUsersAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetUsersAsync_SuperAdmin_Should_Return_All_Users()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        IReadOnlyList<AdminUserDto> users = await service.GetUsersAsync();

        Assert.Equal(4, users.Count);
        Assert.All(users, user => Assert.NotNull(user.DepartmentName ?? user.UserName));
        Assert.Contains(users, user => user.UserName == "viewer-b");
        // 超管自身的 DTO 必须带超管标记，页面对超管隐藏启停开关依赖它。
        AdminUserDto superAdmin = Assert.Single(users, user => user.IsSuperAdmin);
        Assert.Equal("superadmin", superAdmin.UserName);
        Assert.Null(superAdmin.DepartmentId);
    }

    [Fact]
    public async Task GetUsersAsync_DepartmentAdmin_Should_Return_Only_Own_Department()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, DeptAdminActor(seed.DeptAdminA), new FixedTimeProvider(FixedNow));

        IReadOnlyList<AdminUserDto> users = await service.GetUsersAsync();

        Assert.Equal(2, users.Count);
        Assert.All(users, user => Assert.Equal(seed.DeptA.Id, user.DepartmentId));
        Assert.DoesNotContain(users, user => user.UserName == "viewer-b");
    }

    [Fact]
    public async Task GetUsersAsync_NonSuperAdmin_Without_Department_Should_Return_Empty()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);

        // 部门管理员失去部门归属后（例如部门被清理），列表为空而不是全量。
        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            AppUser admin = await setup.AppUsers.SingleAsync(user => user.Id == seed.DeptAdminA.Id);
            admin.DepartmentId = null;
            await setup.SaveChangesAsync();
        }

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, DeptAdminActor(seed.DeptAdminA), new FixedTimeProvider(FixedNow));

        Assert.Empty(await service.GetUsersAsync());
    }

    [Fact]
    public async Task GetUsersAsync_Should_Map_Lockout_And_DisabledSource()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            AppUser viewerA = await setup.AppUsers.SingleAsync(user => user.Id == seed.ViewerA.Id);
            viewerA.LockoutEndAt = FixedNow.AddMinutes(30); // 尚未到期的锁定

            AppUser viewerB = await setup.AppUsers.SingleAsync(user => user.Id == seed.ViewerB.Id);
            viewerB.IsEnabled = false;
            viewerB.DisabledSource = UserDisableSources.Manual;
            await setup.SaveChangesAsync();
        }

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        List<AdminUserDto> users = [.. (await service.GetUsersAsync()).OrderBy(user => user.UserName)];

        Assert.True(users.Single(user => user.UserName == "viewer-a").IsLockedOut);
        Assert.False(users.Single(user => user.UserName == "viewer-b").IsLockedOut);
        Assert.Equal(UserDisableSources.Manual, users.Single(user => user.UserName == "viewer-b").DisabledSource);
        Assert.False(users.Single(user => user.UserName == "viewer-b").IsEnabled);
    }

    /// <summary>
    /// 记录现状：普通用户（无用户管理权限点、已归属部门）直接调用服务时，
    /// 当前实现会返回本部门列表——服务内不校验 UserManagePermission，依赖端点策略拦截。
    /// 属纵深防御缺口，已在汇报中记录；若生产代码收紧此行为，本用例应同步改为期望空列表。
    /// </summary>
    [Fact]
    public async Task GetUsersAsync_PlainUser_WithDepartment_Currently_Returns_Department_List()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        CurrentUser plainViewer = BuildActor(seed.ViewerA);
        AdminUserService service = CreateService(db, plainViewer, new FixedTimeProvider(FixedNow));

        IReadOnlyList<AdminUserDto> users = await service.GetUsersAsync();

        Assert.Equal(2, users.Count);
        Assert.All(users, user => Assert.Equal(seed.DeptA.Id, user.DepartmentId));
    }

    // ------------------------------------------------------------------
    // GetAssignableRolesAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAssignableRolesAsync_SuperAdmin_Should_Get_All_Enabled_Except_SuperAdmin()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        IReadOnlyList<AdminRoleDto> roles = await service.GetAssignableRolesAsync();

        Assert.Equal(
            ["department-admin", "technician", "viewer"],
            roles.Select(role => role.Code).OrderBy(code => code, StringComparer.Ordinal).ToArray());
        // 权限点编码一并返回：部门管理员携带 action.user.manage。
        AdminRoleDto deptAdmin = Assert.Single(roles, role => role.Code == "department-admin");
        Assert.Contains(AuthorizationCatalog.UserManagePermission, deptAdmin.PermissionCodes);
    }

    [Fact]
    public async Task GetAssignableRolesAsync_DepartmentAdmin_Should_Get_Viewer_And_Technician_Only()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, DeptAdminActor(seed.DeptAdminA), new FixedTimeProvider(FixedNow));

        IReadOnlyList<AdminRoleDto> roles = await service.GetAssignableRolesAsync();

        Assert.Equal(["technician", "viewer"], roles.Select(role => role.Code).OrderBy(code => code).ToArray());
    }

    [Fact]
    public async Task GetAssignableRolesAsync_Without_Permission_Should_Return_Empty()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        CurrentUser plainViewer = BuildActor(seed.ViewerA);
        AdminUserService service = CreateService(db, plainViewer, new FixedTimeProvider(FixedNow));

        Assert.Empty(await service.GetAssignableRolesAsync());
    }

    // ------------------------------------------------------------------
    // CreateUserAsync
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("ab", "账号需为 3–32 位小写字母、数字、点、下划线或连字符，且以字母或数字开头。")]
    [InlineData("张三", "账号需为 3–32 位小写字母、数字、点、下划线或连字符，且以字母或数字开头。")]
    public async Task CreateUserAsync_Should_Reject_Invalid_User_Name(string userName, string expectedReason)
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.CreateUserAsync(new AdminUserCreateRequest(
            userName, "新用户", null, null, "Passw0rd123", []));

        Assert.False(result.Succeeded);
        Assert.Equal(expectedReason, result.ErrorMessage);
    }

    [Fact]
    public async Task CreateUserAsync_Should_Reject_Empty_Display_Name()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.CreateUserAsync(new AdminUserCreateRequest(
            "newuser01", "  ", null, null, "Passw0rd123", []));

        Assert.False(result.Succeeded);
        Assert.Equal("请填写姓名。", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateUserAsync_Should_Reject_Weak_Password()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.CreateUserAsync(new AdminUserCreateRequest(
            "newuser01", "新用户", null, null, "short", []));

        Assert.False(result.Succeeded);
        Assert.Equal("密码长度至少 8 位。", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateUserAsync_Should_Reject_Duplicate_Account_After_Normalization()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        // 大小写归一后与已有账号冲突：不允许出现 Admin 与 admin 两个账号。
        AuthOperationResultDto result = await service.CreateUserAsync(new AdminUserCreateRequest(
            "VIEWER-A", "重复账号", null, null, "Passw0rd123", []));

        Assert.False(result.Succeeded);
        Assert.Equal("账号 viewer-a 已存在。", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateUserAsync_SuperAdmin_Should_Create_User_With_Roles_And_Audit()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.CreateUserAsync(new AdminUserCreateRequest(
            "  Zhang.San01  ", "张三", seed.DeptA.Id, " zhang@example.com ",
            "Passw0rd123", [seed.ViewerRoleId, seed.TechnicianRoleId]));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser? created = await verify.AppUsers
            .Include(user => user.UserRoles)
            .ThenInclude(link => link.Role)
            .SingleOrDefaultAsync(user => user.UserName == "zhang.san01");

        Assert.NotNull(created);
        Assert.Equal("张三", created.DisplayName);
        Assert.Equal(seed.DeptA.Id, created.DepartmentId);
        Assert.Equal("zhang@example.com", created.Email);
        Assert.True(created.IsEnabled);
        Assert.True(created.MustChangePassword); // 首次登录强制改密
        Assert.NotNull(created.PasswordHash);
        Assert.Equal(
            ["technician", "viewer"],
            created.UserRoles.Select(link => link.Role!.Code).OrderBy(code => code).ToArray());

        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "user.create");
        Assert.Equal("success", audit.Result);
        Assert.Equal("superadmin", audit.UserName);
        Assert.Equal("zhang.san01", audit.TargetName);
        Assert.Contains("viewer", audit.Detail);
    }

    [Fact]
    public async Task CreateUserAsync_SuperAdmin_Should_Reject_Unknown_Department()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.CreateUserAsync(new AdminUserCreateRequest(
            "newuser01", "新用户", Guid.NewGuid(), null, "Passw0rd123", []));

        Assert.False(result.Succeeded);
        Assert.Equal("所选部门不存在。", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateUserAsync_DepartmentAdmin_Should_Force_Own_Department()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, DeptAdminActor(seed.DeptAdminA), new FixedTimeProvider(FixedNow));

        // 请求里指向二部也无效：部门管理员只能把新账号建在本部门。
        AuthOperationResultDto result = await service.CreateUserAsync(new AdminUserCreateRequest(
            "newuser01", "新用户", seed.DeptB.Id, null, "Passw0rd123", [seed.ViewerRoleId]));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser created = await verify.AppUsers.SingleAsync(user => user.UserName == "newuser01");
        Assert.Equal(seed.DeptA.Id, created.DepartmentId);
    }

    [Fact]
    public async Task CreateUserAsync_DepartmentAdmin_Should_Not_Grant_DepartmentAdmin_Role()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, DeptAdminActor(seed.DeptAdminA), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.CreateUserAsync(new AdminUserCreateRequest(
            "newuser01", "新用户", null, null, "Passw0rd123", [seed.DeptAdminRoleId]));

        Assert.False(result.Succeeded);
        Assert.Equal("角色包含不存在、已停用或超出授权范围的角色。", result.ErrorMessage);

        // 授予部门管理员会形成自我提权链，拒绝必须留痕。
        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "user.create");
        Assert.Equal("failed", audit.Result);
        Assert.Equal("角色包含不存在、已停用或超出授权范围的角色。", DetailReason(audit.Detail));
    }

    [Fact]
    public async Task CreateUserAsync_DepartmentAdmin_Without_Department_Should_Be_Denied()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            AppUser admin = await setup.AppUsers.SingleAsync(user => user.Id == seed.DeptAdminA.Id);
            admin.DepartmentId = null;
            await setup.SaveChangesAsync();
        }

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, DeptAdminActor(seed.DeptAdminA), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.CreateUserAsync(new AdminUserCreateRequest(
            "newuser01", "新用户", null, null, "Passw0rd123", [seed.ViewerRoleId]));

        Assert.False(result.Succeeded);
        Assert.Equal("当前账号未归属部门，无法管理用户。", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateUserAsync_Should_Reject_SuperAdmin_Role_Even_For_SuperAdmin()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        // 超管角色只能通过「移交超管」分配，任何直接分配请求都按越权处理。
        AuthOperationResultDto result = await service.CreateUserAsync(new AdminUserCreateRequest(
            "newuser01", "新用户", null, null, "Passw0rd123", [seed.SuperAdminRoleId]));

        Assert.False(result.Succeeded);
        Assert.Equal("角色包含不存在、已停用或超出授权范围的角色。", result.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // UpdateUserAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task UpdateUserAsync_Should_Reject_Missing_User()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await UpdateUserAsync(service, Guid.NewGuid(), "改名", true);

        Assert.False(result.Succeeded);
        Assert.Equal("账号不存在，请刷新后重试。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateUserAsync_Should_Reject_Empty_Display_Name()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await UpdateUserAsync(service, seed.ViewerA.Id, "", true);

        Assert.False(result.Succeeded);
        Assert.Equal("请填写姓名。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateUserAsync_DepartmentAdmin_Should_Not_Update_Other_Department()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, DeptAdminActor(seed.DeptAdminA), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await UpdateUserAsync(service, seed.ViewerB.Id, "越权改名", true);

        Assert.False(result.Succeeded);
        Assert.Contains("只能管理本部门", result.ErrorMessage);

        // 越权尝试必须留痕，否则审计页上看不到连续试探。
        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "user.update");
        Assert.Equal("failed", audit.Result);
        Assert.Equal("viewer-b", audit.TargetName);
    }

    [Fact]
    public async Task UpdateUserAsync_DepartmentAdmin_Should_Update_Same_Department_User()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, DeptAdminActor(seed.DeptAdminA), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.UpdateUserAsync(
            seed.ViewerA.Id, new AdminUserUpdateRequest("新姓名", "new@example.com", true));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser user = await verify.AppUsers.SingleAsync(item => item.Id == seed.ViewerA.Id);
        Assert.Equal("新姓名", user.DisplayName);
        Assert.Equal("new@example.com", user.Email);
    }

    [Fact]
    public async Task UpdateUserAsync_SuperAdmin_Account_Cannot_Be_Disabled()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await UpdateUserAsync(service, seed.SuperAdmin.Id, "超级管理员", false);

        Assert.False(result.Succeeded);
        Assert.Equal("超级管理员账号不可停用。如需更换请使用「移交超管」。", result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.True(await verify.AppUsers.SingleAsync(user => user.Id == seed.SuperAdmin.Id) is { IsEnabled: true });
    }

    [Fact]
    public async Task UpdateUserAsync_Disable_Should_Clear_State_And_Renew_Security_Stamp()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        Guid stampBefore;

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            AppUser viewerA = await setup.AppUsers.SingleAsync(user => user.Id == seed.ViewerA.Id);
            viewerA.AccessFailedCount = 3;
            viewerA.LockoutEndAt = FixedNow.AddMinutes(15);
            await setup.SaveChangesAsync();
            stampBefore = viewerA.SecurityStamp;
        }

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await UpdateUserAsync(service, seed.ViewerA.Id, "一部用户", false);

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser user = await verify.AppUsers.SingleAsync(item => item.Id == seed.ViewerA.Id);

        Assert.False(user.IsEnabled);
        // 停用来源必须标 manual，目录同步才不会把这个人自动启用回来。
        Assert.Equal(UserDisableSources.Manual, user.DisabledSource);
        // 停用即清空失败计数与锁定，避免启用后仍被剩余锁定期拦在门外。
        Assert.Equal(0, user.AccessFailedCount);
        Assert.Null(user.LockoutEndAt);
        // 停用的目的就是「立刻不让这个人再操作」：换安全戳使旧会话立即失效。
        Assert.NotEqual(stampBefore, user.SecurityStamp);

        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "user.update");
        Assert.Equal("success", audit.Result);
        Assert.Contains("before", audit.Detail);
    }

    [Fact]
    public async Task UpdateUserAsync_Reenable_Should_Clear_DisabledSource()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            AppUser viewerA = await setup.AppUsers.SingleAsync(user => user.Id == seed.ViewerA.Id);
            viewerA.IsEnabled = false;
            viewerA.DisabledSource = UserDisableSources.Directory;
            await setup.SaveChangesAsync();
        }

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await UpdateUserAsync(service, seed.ViewerA.Id, "一部用户", true);

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser user = await verify.AppUsers.SingleAsync(item => item.Id == seed.ViewerA.Id);
        Assert.True(user.IsEnabled);
        Assert.Null(user.DisabledSource);
    }

    [Fact]
    public async Task UpdateUserAsync_NoChange_Should_Not_Write_Audit()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await UpdateUserAsync(service, seed.ViewerA.Id, "一部用户", true);

        Assert.True(result.Succeeded);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Empty(await verify.AuditLogs.ToListAsync());
    }

    // ------------------------------------------------------------------
    // UpdateUserDepartmentAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task UpdateUserDepartmentAsync_DepartmentAdmin_Should_Be_Denied()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, DeptAdminActor(seed.DeptAdminA), new FixedTimeProvider(FixedNow));

        // 跨部门调动不能下放：否则部门管理员可以把别部门的人拉进本部门再处置。
        AuthOperationResultDto result = await service.UpdateUserDepartmentAsync(
            seed.ViewerA.Id, new AdminUserDepartmentRequest(seed.DeptB.Id));

        Assert.False(result.Succeeded);
        Assert.Equal("调整部门仅超级管理员可执行。", result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "user.department.update");
        Assert.Equal("failed", audit.Result);
    }

    [Fact]
    public async Task UpdateUserDepartmentAsync_Without_Permission_Should_Be_Denied()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        CurrentUser plainViewer = BuildActor(seed.ViewerA);
        AdminUserService service = CreateService(db, plainViewer, new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.UpdateUserDepartmentAsync(
            seed.ViewerA.Id, new AdminUserDepartmentRequest(seed.DeptB.Id));

        Assert.False(result.Succeeded);
        Assert.Equal("没有用户管理权限。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateUserDepartmentAsync_SuperAdmin_Should_Move_User()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.UpdateUserDepartmentAsync(
            seed.ViewerA.Id, new AdminUserDepartmentRequest(seed.DeptB.Id));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser user = await verify.AppUsers.SingleAsync(item => item.Id == seed.ViewerA.Id);
        Assert.Equal(seed.DeptB.Id, user.DepartmentId);

        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "user.department.update");
        Assert.Equal("success", audit.Result);
    }

    [Fact]
    public async Task UpdateUserDepartmentAsync_SuperAdmin_Account_Cannot_Be_Moved()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.UpdateUserDepartmentAsync(
            seed.SuperAdmin.Id, new AdminUserDepartmentRequest(seed.DeptA.Id));

        Assert.False(result.Succeeded);
        Assert.Equal("超级管理员账号不可调整部门。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateUserDepartmentAsync_Should_Reject_Unknown_Department()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.UpdateUserDepartmentAsync(
            seed.ViewerA.Id, new AdminUserDepartmentRequest(Guid.NewGuid()));

        Assert.False(result.Succeeded);
        Assert.Equal("所选部门不存在。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateUserDepartmentAsync_Same_Department_Should_Be_NoOp()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.UpdateUserDepartmentAsync(
            seed.ViewerA.Id, new AdminUserDepartmentRequest(seed.DeptA.Id));

        Assert.True(result.Succeeded);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Empty(await verify.AuditLogs.ToListAsync());
    }

    // ------------------------------------------------------------------
    // UpdateUserRolesAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task UpdateUserRolesAsync_SuperAdmin_Target_Should_Be_Rejected()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.UpdateUserRolesAsync(
            seed.SuperAdmin.Id, new AdminUserRolesRequest([seed.ViewerRoleId]));

        Assert.False(result.Succeeded);
        Assert.Equal("超级管理员角色只能通过「移交超管」调整。", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateUserRolesAsync_DepartmentAdmin_Should_Not_Escalate_Roles()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, DeptAdminActor(seed.DeptAdminA), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.UpdateUserRolesAsync(
            seed.ViewerA.Id, new AdminUserRolesRequest([seed.DeptAdminRoleId]));

        Assert.False(result.Succeeded);
        Assert.Equal("角色包含不存在、已停用或超出授权范围的角色。", result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "user.roles.update");
        Assert.Equal("failed", audit.Result);
    }

    [Fact]
    public async Task UpdateUserRolesAsync_Should_Replace_Roles()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.UpdateUserRolesAsync(
            seed.ViewerA.Id, new AdminUserRolesRequest([seed.TechnicianRoleId]));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser user = await verify.AppUsers
            .Include(item => item.UserRoles)
            .ThenInclude(link => link.Role)
            .SingleAsync(item => item.Id == seed.ViewerA.Id);

        // 全量覆盖：viewer 已被替换掉。
        Assert.Equal(["technician"], user.UserRoles.Select(link => link.Role!.Code).ToArray());

        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "user.roles.update");
        Assert.Equal("success", audit.Result);
        Assert.Contains("\"before\":[\"viewer\"]", audit.Detail);
        Assert.Contains("\"after\":[\"technician\"]", audit.Detail);
    }

    [Fact]
    public async Task UpdateUserRolesAsync_Same_Roles_Should_Be_NoOp()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.UpdateUserRolesAsync(
            seed.ViewerA.Id, new AdminUserRolesRequest([seed.ViewerRoleId]));

        Assert.True(result.Succeeded);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Empty(await verify.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task UpdateUserRolesAsync_Should_Reject_Unknown_Role()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.UpdateUserRolesAsync(
            seed.ViewerA.Id, new AdminUserRolesRequest([Guid.NewGuid()]));

        Assert.False(result.Succeeded);
        Assert.Equal("角色包含不存在、已停用或超出授权范围的角色。", result.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // ResetPasswordAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResetPasswordAsync_Should_Set_MustChange_And_Renew_Stamp()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        Guid stampBefore;
        string hashBefore;

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            AppUser viewerA = await setup.AppUsers.SingleAsync(user => user.Id == seed.ViewerA.Id);
            viewerA.AccessFailedCount = 5;
            viewerA.LockoutEndAt = FixedNow.AddMinutes(10);
            stampBefore = viewerA.SecurityStamp;
            hashBefore = viewerA.PasswordHash!;
            await setup.SaveChangesAsync();
        }

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.ResetPasswordAsync(
            seed.ViewerA.Id, new AdminUserResetPasswordRequest("NewPassw0rd456"));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser user = await verify.AppUsers.SingleAsync(item => item.Id == seed.ViewerA.Id);

        Assert.NotEqual(hashBefore, user.PasswordHash);
        Assert.True(user.MustChangePassword); // 重置后首次登录强制改密
        Assert.Equal(0, user.AccessFailedCount);
        Assert.Null(user.LockoutEndAt);
        // 重置密码意味着账号可能被冒用：已签发会话全部作废。
        Assert.NotEqual(stampBefore, user.SecurityStamp);

        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "user.password.reset");
        Assert.Equal("success", audit.Result);
    }

    [Fact]
    public async Task ResetPasswordAsync_SuperAdmin_Password_Cannot_Be_Reset_By_Others()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.ResetPasswordAsync(
            seed.SuperAdmin.Id, new AdminUserResetPasswordRequest("NewPassw0rd456"));

        Assert.False(result.Succeeded);
        Assert.Equal("超级管理员密码只能由本人修改。", result.ErrorMessage);
    }

    [Fact]
    public async Task ResetPasswordAsync_DepartmentAdmin_Should_Not_Reset_Other_Department()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, DeptAdminActor(seed.DeptAdminA), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.ResetPasswordAsync(
            seed.ViewerB.Id, new AdminUserResetPasswordRequest("NewPassw0rd456"));

        Assert.False(result.Succeeded);
        Assert.Contains("只能管理本部门", result.ErrorMessage);
    }

    [Fact]
    public async Task ResetPasswordAsync_Should_Validate_Password()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.ResetPasswordAsync(
            seed.ViewerA.Id, new AdminUserResetPasswordRequest("viewer-a"));

        Assert.False(result.Succeeded);
        Assert.Equal("密码不能与账号相同。", result.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // SetUsersEnabledAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task SetUsersEnabledAsync_Empty_Selection_Should_Be_Rejected()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.SetUsersEnabledAsync(new AdminUserBatchStatusRequest([], false));

        Assert.False(result.Succeeded);
        Assert.Equal("请先选择账号。", result.ErrorMessage);
    }

    [Fact]
    public async Task SetUsersEnabledAsync_Partial_Missing_Should_Be_Rejected()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.SetUsersEnabledAsync(
            new AdminUserBatchStatusRequest([seed.ViewerA.Id, Guid.NewGuid()], false));

        Assert.False(result.Succeeded);
        Assert.Equal("部分账号不存在，请刷新后重试。", result.ErrorMessage);
    }

    [Fact]
    public async Task SetUsersEnabledAsync_DepartmentAdmin_Should_Reject_Cross_Department()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, DeptAdminActor(seed.DeptAdminA), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.SetUsersEnabledAsync(
            new AdminUserBatchStatusRequest([seed.ViewerA.Id, seed.ViewerB.Id], false));

        Assert.False(result.Succeeded);
        Assert.Contains("只能管理本部门", result.ErrorMessage);
    }

    [Fact]
    public async Task SetUsersEnabledAsync_Disable_Should_Reject_SuperAdmin_In_Batch()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        // 命中超管账号时整批拒绝：部分成功会让操作者误以为全部生效。
        AuthOperationResultDto result = await service.SetUsersEnabledAsync(
            new AdminUserBatchStatusRequest([seed.ViewerA.Id, seed.SuperAdmin.Id], false));

        Assert.False(result.Succeeded);
        Assert.Equal("超级管理员账号不可停用。", result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        // 整批拒绝：viewer-a 也不能被顺带停用。
        Assert.True(await verify.AppUsers.SingleAsync(user => user.Id == seed.ViewerA.Id) is { IsEnabled: true });
    }

    [Fact]
    public async Task SetUsersEnabledAsync_Disable_Should_Renew_Stamp_And_Mark_Manual()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        Guid stampBefore;

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            stampBefore = (await setup.AppUsers.SingleAsync(user => user.Id == seed.ViewerA.Id)).SecurityStamp;
        }

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.SetUsersEnabledAsync(
            new AdminUserBatchStatusRequest([seed.ViewerA.Id, seed.ViewerB.Id], false));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        foreach (Guid userId in new[] { seed.ViewerA.Id, seed.ViewerB.Id })
        {
            AppUser user = await verify.AppUsers.SingleAsync(item => item.Id == userId);
            Assert.False(user.IsEnabled);
            Assert.Equal(UserDisableSources.Manual, user.DisabledSource);
        }

        Assert.NotEqual(stampBefore,
            (await verify.AppUsers.SingleAsync(user => user.Id == seed.ViewerA.Id)).SecurityStamp);

        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "user.status.batch");
        Assert.Equal("success", audit.Result);
        Assert.Contains("viewer-a", audit.Detail);
        Assert.Contains("viewer-b", audit.Detail);
    }

    [Fact]
    public async Task SetUsersEnabledAsync_NoStateChange_Should_Not_Write_Audit()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        // 全员已启用，再批量启用是空操作。
        AuthOperationResultDto result = await service.SetUsersEnabledAsync(
            new AdminUserBatchStatusRequest([seed.ViewerA.Id], true));

        Assert.True(result.Succeeded);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Empty(await verify.AuditLogs.ToListAsync());
    }

    // ------------------------------------------------------------------
    // HandoverSuperAdminAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task HandoverSuperAdminAsync_NonSuperAdmin_Should_Be_Denied()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, DeptAdminActor(seed.DeptAdminA), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.HandoverSuperAdminAsync(
            new SuperAdminHandoverRequest(seed.ViewerA.Id));

        Assert.False(result.Succeeded);
        Assert.Equal("只有超级管理员可以移交账号。", result.ErrorMessage);
    }

    [Fact]
    public async Task HandoverSuperAdminAsync_Self_Should_Be_Rejected()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.HandoverSuperAdminAsync(
            new SuperAdminHandoverRequest(seed.SuperAdmin.Id));

        Assert.False(result.Succeeded);
        Assert.Equal("接任账号不能是当前账号。", result.ErrorMessage);
    }

    [Fact]
    public async Task HandoverSuperAdminAsync_Missing_Target_Should_Be_Rejected()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.HandoverSuperAdminAsync(
            new SuperAdminHandoverRequest(Guid.NewGuid()));

        Assert.False(result.Succeeded);
        Assert.Equal("接任账号不存在，请刷新后重试。", result.ErrorMessage);
    }

    [Fact]
    public async Task HandoverSuperAdminAsync_Disabled_Target_Should_Be_Rejected()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            AppUser viewerA = await setup.AppUsers.SingleAsync(user => user.Id == seed.ViewerA.Id);
            viewerA.IsEnabled = false;
            await setup.SaveChangesAsync();
        }

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.HandoverSuperAdminAsync(
            new SuperAdminHandoverRequest(seed.ViewerA.Id));

        Assert.False(result.Succeeded);
        Assert.Equal("接任账号已停用，请先启用再移交。", result.ErrorMessage);
    }

    [Fact]
    public async Task HandoverSuperAdminAsync_Existing_SuperAdmin_Target_Should_Be_Rejected()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.HandoverSuperAdminAsync(
            new SuperAdminHandoverRequest(seed.SuperAdmin.Id));

        Assert.False(result.Succeeded);
        Assert.Equal("接任账号不能是当前账号。", result.ErrorMessage);
    }

    [Fact]
    public async Task HandoverSuperAdminAsync_Should_Transfer_Role_And_Renew_Both_Stamps()
    {
        using SqliteTestDatabase database = new();
        SeedData seed = Seed(database);
        Guid actorStampBefore;
        Guid targetStampBefore;

        await using (DataAcquisitionPlatformDbContext setup = database.CreateContext())
        {
            actorStampBefore = (await setup.AppUsers.SingleAsync(user => user.Id == seed.SuperAdmin.Id)).SecurityStamp;
            targetStampBefore = (await setup.AppUsers.SingleAsync(user => user.Id == seed.ViewerA.Id)).SecurityStamp;
        }

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AdminUserService service = CreateService(db, SuperAdminActor(seed.SuperAdmin), new FixedTimeProvider(FixedNow));

        AuthOperationResultDto result = await service.HandoverSuperAdminAsync(
            new SuperAdminHandoverRequest(seed.ViewerA.Id));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();

        // 同一事务内先降级原账号再升级目标账号，不出现「零超管」中间态。
        List<AppUser> users = await verify.AppUsers
            .Include(user => user.UserRoles)
            .ThenInclude(link => link.Role)
            .Where(user => user.Id == seed.SuperAdmin.Id || user.Id == seed.ViewerA.Id)
            .ToListAsync();

        Assert.DoesNotContain(
            users.Single(user => user.Id == seed.SuperAdmin.Id).UserRoles,
            link => link.Role!.Code == AuthorizationCatalog.SuperAdminRole);
        Assert.Equal([AuthorizationCatalog.SuperAdminRole],
            users.Single(user => user.Id == seed.ViewerA.Id)
                .UserRoles.Where(link => link.Role!.Code == AuthorizationCatalog.SuperAdminRole)
                .Select(link => link.Role!.Code).ToList());

        // 两边都换安全戳：旧超管的会话必须立刻结束，接任者也要重新登录拿超管权限。
        Assert.NotEqual(actorStampBefore,
            users.Single(user => user.Id == seed.SuperAdmin.Id).SecurityStamp);
        Assert.NotEqual(targetStampBefore,
            users.Single(user => user.Id == seed.ViewerA.Id).SecurityStamp);

        AuditLog audit = await verify.AuditLogs.SingleAsync(log => log.ActionCode == "user.super_admin.handover");
        Assert.Equal("success", audit.Result);
        Assert.Contains("superadmin", audit.Detail);
        Assert.Contains("viewer-a", audit.Detail);
    }
}
