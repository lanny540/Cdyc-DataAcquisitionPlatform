using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security.External;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAP.Tests.UnitTests;

/// <summary>
/// OIDC 首次登录 JIT 建号与字段刷新的单元测试。
///
/// 关键规则：多 IdP 的身份键必须带提供方编码；账号名冲突时用「账号-提供方」
/// 后缀而不是并号；部门映射不到时保留原归属。
/// </summary>
public sealed class OidcSignInServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    private static OidcProviderOptions Provider(string code = "keycloak",
        string defaultRole = AuthorizationCatalog.ViewerRole)
    {
        return new OidcProviderOptions
        {
            Code = code,
            DisplayName = "统一登录",
            Authority = "https://sso.example.com",
            ClientId = "dap-client",
            DefaultRoleCode = defaultRole
        };
    }

    private static OidcSignInService CreateService(DataAcquisitionPlatformDbContext db)
    {
        return new OidcSignInService(
            db,
            new DepartmentCodeResolver(db),
            new FixedTimeProvider(Now),
            NullLogger<OidcSignInService>.Instance);
    }

    private static OidcUserProfile Profile(
        string subject = "sub-1",
        string userName = "alice",
        string displayName = "爱丽丝",
        string? email = "alice@example.com",
        string? departmentCode = null)
    {
        return new OidcUserProfile(subject, userName, displayName, email, departmentCode);
    }

    private static Department SeedDepartment(string externalCode)
    {
        return new Department
        {
            Id = Guid.NewGuid(),
            Code = $"D-{externalCode}",
            Name = externalCode,
            ExternalCode = externalCode,
            IsEnabled = true,
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }

    private static Role SeedRole(string code, bool enabled = true)
    {
        return new Role
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            PageLevel = 1,
            IsBuiltin = true,
            IsEnabled = enabled,
            SortOrder = 10,
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }

    private static AppUser SeedUser(string userName, bool enabled = true)
    {
        return new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            DisplayName = userName,
            IsEnabled = enabled,
            MustChangePassword = false,
            PasswordHash = null,
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }

    [Fact]
    public async Task ProvisionAsync_First_Login_Should_Create_User_With_Default_Role()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Department workshop = SeedDepartment("STAFF");
        db.Departments.Add(workshop);
        db.Roles.Add(SeedRole(AuthorizationCatalog.ViewerRole));
        await db.SaveChangesAsync();

        OidcSignInService service = CreateService(db);
        OidcProvisionResult result = await service.ProvisionAsync(
            Provider(), Profile(departmentCode: "STAFF"));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotNull(result.UserId);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser created = await verify.AppUsers
            .Include(user => user.ExternalIdentities)
            .Include(user => user.UserRoles)
            .ThenInclude(link => link.Role)
            .SingleAsync(user => user.Id == result.UserId);

        Assert.Equal("alice", created.UserName);
        Assert.Equal("爱丽丝", created.DisplayName);
        Assert.Equal("alice@example.com", created.Email);
        Assert.Equal(workshop.Id, created.DepartmentId);
        Assert.True(created.IsEnabled);
        Assert.Null(created.PasswordHash);
        Assert.Contains(created.UserRoles, link => link.Role!.Code == AuthorizationCatalog.ViewerRole);

        // 身份键带提供方编码：两家 IdP 的 sub 撞在一起也不能串号。
        ExternalIdentity identity = created.ExternalIdentities.Single();
        Assert.Equal("oidc:keycloak", identity.Provider);
        Assert.Equal("sub-1", identity.ExternalId);
        Assert.Equal(Now, identity.LastSyncedAt);
    }

    [Fact]
    public async Task ProvisionAsync_Second_Login_Should_Refresh_Fields()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Department staff = SeedDepartment("STAFF");
        Department energy = SeedDepartment("ENERGY");
        db.Departments.Add(staff);
        db.Departments.Add(energy);

        AppUser user = SeedUser("alice");
        user.DisplayName = "爱丽丝";
        user.Email = "alice@example.com";
        user.DepartmentId = staff.Id;
        db.AppUsers.Add(user);
        db.ExternalIdentities.Add(new ExternalIdentity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Provider = "oidc:keycloak",
            ExternalId = "sub-1",
            LastSyncedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now
        });
        await db.SaveChangesAsync();

        OidcSignInService service = CreateService(db);
        OidcProvisionResult result = await service.ProvisionAsync(
            Provider(), Profile(displayName: "爱丽丝·张", email: "alice2@example.com", departmentCode: "ENERGY"));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(user.Id, result.UserId);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser updated = await verify.AppUsers.SingleAsync(item => item.Id == user.Id);
        Assert.Equal("爱丽丝·张", updated.DisplayName);
        Assert.Equal("alice2@example.com", updated.Email);
        // 部门只在映射到时更新。
        Assert.Equal(energy.Id, updated.DepartmentId);
        Assert.Equal(Now, updated.UpdatedAt);
    }

    [Fact]
    public async Task ProvisionAsync_Unmapped_Department_Should_Keep_Original()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Department staff = SeedDepartment("STAFF");
        db.Departments.Add(staff);

        AppUser user = SeedUser("alice");
        user.DepartmentId = staff.Id;
        db.AppUsers.Add(user);
        db.ExternalIdentities.Add(new ExternalIdentity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Provider = "oidc:keycloak",
            ExternalId = "sub-1",
            LastSyncedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now
        });
        await db.SaveChangesAsync();

        OidcSignInService service = CreateService(db);
        OidcProvisionResult result = await service.ProvisionAsync(
            Provider(), Profile(departmentCode: "UNKNOWN"));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        Assert.Equal(staff.Id, (await verify.AppUsers.SingleAsync(item => item.Id == user.Id)).DepartmentId);
    }

    [Fact]
    public async Task ProvisionAsync_Disabled_User_Should_Be_Rejected()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser user = SeedUser("alice", false);
        db.AppUsers.Add(user);
        db.ExternalIdentities.Add(new ExternalIdentity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Provider = "oidc:keycloak",
            ExternalId = "sub-1",
            LastSyncedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now
        });
        await db.SaveChangesAsync();

        OidcSignInService service = CreateService(db);
        OidcProvisionResult result = await service.ProvisionAsync(Provider(), Profile());

        Assert.False(result.Succeeded);
        Assert.Equal("该账号已停用，请联系管理员。", result.ErrorMessage);
    }

    // 「身份绑定指向已删除账号」的防御分支未覆盖：外键约束使 SQLite 测试库无法构造该状态
    // （生产 PostgreSQL 同样受 FK 约束，仅在历史脏数据下可能出现）。

    [Fact]
    public async Task ProvisionAsync_Name_Conflict_Should_Append_Provider_Suffix()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.Roles.Add(SeedRole(AuthorizationCatalog.ViewerRole));
        // 本地已有 alice：不能并号，否则外部身份会挂到别人的账号上。
        db.AppUsers.Add(SeedUser("alice"));
        await db.SaveChangesAsync();

        OidcSignInService service = CreateService(db);
        OidcProvisionResult result = await service.ProvisionAsync(Provider(), Profile());

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser created = await verify.AppUsers.SingleAsync(item => item.Id == result.UserId!.Value);
        Assert.Equal("alice-keycloak", created.UserName);
    }

    [Fact]
    public async Task ProvisionAsync_Repeated_Name_Conflicts_Should_Append_Sequence()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.Roles.Add(SeedRole(AuthorizationCatalog.ViewerRole));
        db.AppUsers.Add(SeedUser("alice"));
        db.AppUsers.Add(SeedUser("alice-keycloak"));
        await db.SaveChangesAsync();

        OidcSignInService service = CreateService(db);
        OidcProvisionResult result = await service.ProvisionAsync(Provider(), Profile());

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser created = await verify.AppUsers.SingleAsync(item => item.Id == result.UserId!.Value);
        // 首选「账号-提供方」被占用后：再加序号。
        Assert.Equal("alice-keycloak3", created.UserName);
    }

    [Fact]
    public async Task ProvisionAsync_Blank_UserName_Should_Fall_Back_To_Provider_Stem()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.Roles.Add(SeedRole(AuthorizationCatalog.ViewerRole));
        await db.SaveChangesAsync();

        OidcSignInService service = CreateService(db);
        OidcProvisionResult result = await service.ProvisionAsync(
            Provider(), Profile(userName: "", displayName: ""));

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser created = await verify.AppUsers.SingleAsync(item => item.Id == result.UserId!.Value);
        Assert.Equal("oidc-keycloak", created.UserName);
        // 姓名为空时用账号名兜底。
        Assert.Equal("oidc-keycloak", created.DisplayName);
    }

    [Fact]
    public async Task ProvisionAsync_Unavailable_Default_Role_Should_Create_Without_Roles()
    {
        using SqliteTestDatabase database = new();
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.Roles.Add(SeedRole(AuthorizationCatalog.ViewerRole, false));
        await db.SaveChangesAsync();

        OidcSignInService service = CreateService(db);
        OidcProvisionResult result = await service.ProvisionAsync(Provider(), Profile());

        Assert.True(result.Succeeded, result.ErrorMessage);

        await using DataAcquisitionPlatformDbContext verify = database.CreateContext();
        AppUser created = await verify.AppUsers
            .Include(user => user.UserRoles)
            .SingleAsync(user => user.Id == result.UserId!.Value);
        Assert.Empty(created.UserRoles);
    }

    [Fact]
    public void ProviderKey_Should_Include_Provider_Code()
    {
        Assert.Equal("oidc:keycloak", OidcSignInService.ProviderKey(Provider()));
        Assert.Equal("oidc:portal", OidcSignInService.ProviderKey(Provider("portal")));
    }
}
