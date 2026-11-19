using DAP.Core.Domain.Entities;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Catalog;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 认证数据初始化器的单元测试。
///
/// 任何一步失败都会阻止应用启动：角色或超管缺失的结果不是「功能少了一点」，
/// 而是所有人都进不去且现场没有入口可修。这里把「种子、对账、自检、拒启」四类行为全部钉住。
/// </summary>
public sealed class AuthDataInitializerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);
    private const string BootstrapPassword = "Initial@123";

    /// <summary>
    /// 带固定 TimeProvider 的作用域工厂：初始化器从容器里解析 TimeProvider。
    /// </summary>
    private static IServiceScopeFactory CreateScopeFactory(SqliteTestDatabase database, TimeProvider timeProvider)
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.ClearProviders());
        services.AddScoped<DataAcquisitionPlatformDbContext>(_ => database.CreateContext());
        services.AddSingleton(timeProvider);
        IServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private static AuthDataInitializer CreateInitializer(
        SqliteTestDatabase database,
        PlatformAuthOptions options,
        IHostEnvironment environment,
        TimeProvider? timeProvider = null)
    {
        return new AuthDataInitializer(
            CreateScopeFactory(database, timeProvider ?? new FixedTimeProvider(Now)),
            Options.Create(options),
            environment,
            NullLogger<AuthDataInitializer>.Instance);
    }

    private static PlatformAuthOptions OptionsWithBootstrap(
        string userName = "bootadmin",
        string password = BootstrapPassword)
    {
        return new PlatformAuthOptions
        {
            BootstrapAdmin = { UserName = userName, Password = password, DisplayName = "启动超管" }
        };
    }

    private static AppUser CreateUser(string userName, string password, bool enabled = true)
    {
        return new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            DisplayName = userName,
            IsEnabled = enabled,
            MustChangePassword = false,
            PasswordHash = PasswordHasher.Hash(password),
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }

    private static Role CreateRole(string code, bool enabled)
    {
        return new Role
        {
            Id = Guid.NewGuid(), Code = code, Name = code, PageLevel = 1,
            IsBuiltin = true, IsEnabled = enabled, SortOrder = 10,
            CreatedAt = Now, UpdatedAt = Now
        };
    }

    // ---------- 种子与对账 ----------

    [Fact]
    public async Task StartAsync_Should_Seed_Builtins_And_Bootstrap_Admin()
    {
        using SqliteTestDatabase database = new();
        AuthDataInitializer initializer = CreateInitializer(
            database, OptionsWithBootstrap(), HostingFakes.CreateDevelopmentEnvironment());

        await initializer.StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        // 内置角色、权限点、职能域按目录种子。
        Assert.Equal(4, await db.Roles.CountAsync());
        Assert.Equal(5, await db.Permissions.CountAsync());
        Assert.Equal(4, await db.BusinessDomains.CountAsync());
        Assert.Contains(AuthorizationCatalog.PlatformDomain,
            await db.BusinessDomains.Select(item => item.Code).ToListAsync());

        // 页面归属对账：代码里声明的页面全部入库。
        Assert.True(await db.DomainPages.AnyAsync(page => page.IsEnabled && !page.IsOrphaned));

        // 角色权限关联：技术人员带排班配置权限点。
        Role technician = await db.Roles.SingleAsync(role => role.Code == AuthorizationCatalog.TechnicianRole);
        List<string> technicianPermissions = await db.RolePermissions
            .Where(link => link.RoleId == technician.Id)
            .Join(db.Permissions, link => link.PermissionId, permission => permission.Id,
                (link, permission) => permission.Code)
            .ToListAsync();
        Assert.Equal([AuthorizationCatalog.ShiftManagePermission], technicianPermissions);

        // 初始超管：启用、首登强制改密、密码可校验。
        AppUser admin = await db.AppUsers.SingleAsync(user => user.UserName == "bootadmin");
        Assert.True(admin.IsEnabled);
        Assert.True(admin.MustChangePassword);
        Assert.True(PasswordHasher.Verify(BootstrapPassword, admin.PasswordHash!));
        Assert.Equal("启动超管", admin.DisplayName);

        Role superAdminRole = await db.Roles.SingleAsync(role => role.Code == AuthorizationCatalog.SuperAdminRole);
        Assert.True(await db.UserRoles.AnyAsync(link =>
            link.UserId == admin.Id && link.RoleId == superAdminRole.Id));
    }

    [Fact]
    public async Task StartAsync_Should_Be_Idempotent_When_Run_Twice()
    {
        using SqliteTestDatabase database = new();
        AuthDataInitializer initializer = CreateInitializer(
            database, OptionsWithBootstrap(), HostingFakes.CreateDevelopmentEnvironment());

        await initializer.StartAsync(CancellationToken.None);
        await initializer.StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Assert.Equal(4, await db.Roles.CountAsync());
        Assert.Equal(5, await db.Permissions.CountAsync());
        Assert.Equal(4, await db.BusinessDomains.CountAsync());
        Assert.Equal(1, await db.AppUsers.CountAsync());

        Role superAdminRole = await db.Roles.SingleAsync(role => role.Code == AuthorizationCatalog.SuperAdminRole);
        Assert.Equal(1, await db.UserRoles.CountAsync(link => link.RoleId == superAdminRole.Id));
    }

    [Fact]
    public async Task StartAsync_Should_Reconcile_Role_Permissions()
    {
        using SqliteTestDatabase database = new();
        AuthDataInitializer initializer = CreateInitializer(
            database, OptionsWithBootstrap(), HostingFakes.CreateDevelopmentEnvironment());
        await initializer.StartAsync(CancellationToken.None);

        // 手工给普通用户角色塞一个不属于它的权限点：下一次启动对账应移除。
        await using (DataAcquisitionPlatformDbContext seed = database.CreateContext())
        {
            Role viewer = await seed.Roles.SingleAsync(role => role.Code == AuthorizationCatalog.ViewerRole);
            Permission rogue = await seed.Permissions
                .SingleAsync(permission => permission.Code == AuthorizationCatalog.SystemManagePermission);
            seed.RolePermissions.Add(new RolePermission { RoleId = viewer.Id, PermissionId = rogue.Id });
            await seed.SaveChangesAsync();
        }

        await initializer.StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Role viewerRole = await db.Roles.SingleAsync(role => role.Code == AuthorizationCatalog.ViewerRole);
        Assert.Empty(await db.RolePermissions.Where(link => link.RoleId == viewerRole.Id).ToListAsync());
    }

    // ---------- 超管自检（数量 ≠ 1 拒启） ----------

    [Fact]
    public async Task StartAsync_Without_Super_Admin_And_Bootstrap_Should_Refuse_To_Start()
    {
        using SqliteTestDatabase database = new();
        AuthDataInitializer initializer = CreateInitializer(
            database, new PlatformAuthOptions(), HostingFakes.CreateDevelopmentEnvironment());

        // 空库 + 未配置 BootstrapAdmin：否则首次上线将无人能登录。
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.StartAsync(CancellationToken.None));
        Assert.Contains("超级管理员", exception.Message);
    }

    [Fact]
    public async Task StartAsync_With_Two_Super_Admins_Should_Refuse_To_Start()
    {
        using SqliteTestDatabase database = new();
        await using (DataAcquisitionPlatformDbContext seed = database.CreateContext())
        {
            Role superAdminRole = CreateRole(AuthorizationCatalog.SuperAdminRole, true);
            seed.Roles.Add(superAdminRole);
            AppUser first = CreateUser("first", BootstrapPassword);
            AppUser second = CreateUser("second", BootstrapPassword);
            seed.AppUsers.Add(first);
            seed.AppUsers.Add(second);
            seed.UserRoles.Add(new UserRole { UserId = first.Id, RoleId = superAdminRole.Id });
            seed.UserRoles.Add(new UserRole { UserId = second.Id, RoleId = superAdminRole.Id });
            await seed.SaveChangesAsync();
        }

        AuthDataInitializer initializer = CreateInitializer(
            database, OptionsWithBootstrap(), HostingFakes.CreateDevelopmentEnvironment());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.StartAsync(CancellationToken.None));
        Assert.Contains("存在 2 个启用状态的超级管理员", exception.Message);
    }

    [Fact]
    public async Task StartAsync_Should_Ignore_Bootstrap_When_Super_Admin_Exists()
    {
        using SqliteTestDatabase database = new();
        await using (DataAcquisitionPlatformDbContext seed = database.CreateContext())
        {
            Role superAdminRole = CreateRole(AuthorizationCatalog.SuperAdminRole, true);
            seed.Roles.Add(superAdminRole);
            AppUser existing = CreateUser("existing", "Existing@123");
            seed.AppUsers.Add(existing);
            seed.UserRoles.Add(new UserRole { UserId = existing.Id, RoleId = superAdminRole.Id });
            await seed.SaveChangesAsync();
        }

        // 库中已存在超管时 BootstrapAdmin 配置被忽略：不会额外建第二个。
        AuthDataInitializer initializer = CreateInitializer(
            database, OptionsWithBootstrap(), HostingFakes.CreateDevelopmentEnvironment());
        await initializer.StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser existingAdmin = await db.AppUsers.SingleAsync(user => user.UserName == "existing");
        Assert.Null(await db.AppUsers.FirstOrDefaultAsync(user => user.UserName == "bootadmin"));
        // 已有超管的密码不被覆盖。
        Assert.True(PasswordHasher.Verify("Existing@123", existingAdmin.PasswordHash!));
    }

    [Fact]
    public async Task StartAsync_Should_Rescue_Existing_User_Without_Super_Admin_Role()
    {
        using SqliteTestDatabase database = new();
        await using (DataAcquisitionPlatformDbContext seed = database.CreateContext())
        {
            // 账号已存在（历史遗留）但没有超管角色且已停用：零超管的自救路径。
            seed.AppUsers.Add(CreateUser("bootadmin", "Old@12345", enabled: false));
            await seed.SaveChangesAsync();
        }

        AuthDataInitializer initializer = CreateInitializer(
            database, OptionsWithBootstrap(), HostingFakes.CreateDevelopmentEnvironment());
        await initializer.StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser rescued = await db.AppUsers.SingleAsync(user => user.UserName == "bootadmin");
        Assert.True(rescued.IsEnabled);
        Assert.True(rescued.MustChangePassword);
        Assert.True(PasswordHasher.Verify(BootstrapPassword, rescued.PasswordHash!));
        Role superAdminRole = await db.Roles.SingleAsync(role => role.Code == AuthorizationCatalog.SuperAdminRole);
        Assert.True(await db.UserRoles.AnyAsync(link =>
            link.UserId == rescued.Id && link.RoleId == superAdminRole.Id));
    }

    // ---------- 生产环境默认密码拒启 ----------

    [Fact]
    public async Task StartAsync_Default_Bootstrap_Password_In_Production_Should_Refuse_To_Start()
    {
        using SqliteTestDatabase database = new();
        AuthDataInitializer initializer = CreateInitializer(
            database,
            OptionsWithBootstrap(password: "Admin@123456"),
            HostingFakes.CreateProductionEnvironment());

        // appsettings.json 里的公开默认密码只能用于开发环境。
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.StartAsync(CancellationToken.None));
        Assert.Contains("默认初始密码", exception.Message);
    }

    [Fact]
    public async Task StartAsync_Custom_Password_In_Production_Should_Succeed()
    {
        using SqliteTestDatabase database = new();
        AuthDataInitializer initializer = CreateInitializer(
            database,
            OptionsWithBootstrap(password: "ProdSecret@123"),
            HostingFakes.CreateProductionEnvironment());

        await initializer.StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        AppUser admin = await db.AppUsers.SingleAsync(user => user.UserName == "bootadmin");
        Assert.True(PasswordHasher.Verify("ProdSecret@123", admin.PasswordHash!));
    }

    [Fact]
    public async Task StartAsync_Weak_Bootstrap_Password_Should_Refuse_To_Start()
    {
        using SqliteTestDatabase database = new();
        AuthDataInitializer initializer = CreateInitializer(
            database,
            OptionsWithBootstrap(password: "weak"),
            HostingFakes.CreateDevelopmentEnvironment());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.StartAsync(CancellationToken.None));
        Assert.Contains("不满足要求", exception.Message);
    }

    // ---------- 部门管理员开关（只朝启用方向生效） ----------

    [Fact]
    public async Task StartAsync_Department_Admin_Switch_Should_Enable_BuiltIn_Role()
    {
        using SqliteTestDatabase database = new();
        PlatformAuthOptions options = OptionsWithBootstrap();
        AuthDataInitializer initializer = CreateInitializer(
            database, options, HostingFakes.CreateDevelopmentEnvironment());
        await initializer.StartAsync(CancellationToken.None);

        // 手工把部门管理员角色停用，再按开关启用。
        await using (DataAcquisitionPlatformDbContext seed = database.CreateContext())
        {
            Role role = await seed.Roles.SingleAsync(item => item.Code == AuthorizationCatalog.DepartmentAdminRole);
            role.IsEnabled = false;
            await seed.SaveChangesAsync();
        }

        options.DepartmentAdmin.Enabled = true;
        await initializer.StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Assert.True((await db.Roles.SingleAsync(item =>
            item.Code == AuthorizationCatalog.DepartmentAdminRole)).IsEnabled);
    }

    [Fact]
    public async Task StartAsync_Department_Admin_Switch_Off_Should_Not_Disable_Manually_Enabled_Role()
    {
        using SqliteTestDatabase database = new();
        PlatformAuthOptions options = OptionsWithBootstrap();
        AuthDataInitializer initializer = CreateInitializer(
            database, options, HostingFakes.CreateDevelopmentEnvironment());
        await initializer.StartAsync(CancellationToken.None);

        // 管理员手工启用部门管理员角色后，把开关关回 false：
        // 人工决定优先于默认值，一次重启不能让现场失去入口。
        await using (DataAcquisitionPlatformDbContext seed = database.CreateContext())
        {
            Role role = await seed.Roles.SingleAsync(item => item.Code == AuthorizationCatalog.DepartmentAdminRole);
            role.IsEnabled = true;
            await seed.SaveChangesAsync();
        }

        options.DepartmentAdmin.Enabled = false;
        await initializer.StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Assert.True((await db.Roles.SingleAsync(item =>
            item.Code == AuthorizationCatalog.DepartmentAdminRole)).IsEnabled);
    }

    // ---------- 外部身份源与车间终端配置校验（半配置拒启） ----------

    [Fact]
    public async Task StartAsync_Board_Enabled_With_Empty_Whitelist_Should_Refuse_To_Start()
    {
        using SqliteTestDatabase database = new();
        PlatformAuthOptions options = OptionsWithBootstrap();
        options.Board.Enabled = true;
        options.Board.IpWhitelist = [];

        AuthDataInitializer initializer = CreateInitializer(
            database, options, HostingFakes.CreateDevelopmentEnvironment());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.StartAsync(CancellationToken.None));
        Assert.Contains("IpWhitelist", exception.Message);
    }

    [Fact]
    public async Task StartAsync_Incomplete_Ldap_Should_Refuse_To_Start()
    {
        using SqliteTestDatabase database = new();
        PlatformAuthOptions options = OptionsWithBootstrap();
        options.Ldap.Enabled = true;

        AuthDataInitializer initializer = CreateInitializer(
            database, options, HostingFakes.CreateDevelopmentEnvironment());

        // 半配置的目录认证会让域账号登录失败，且现场看起来和「密码错了」一模一样。
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.StartAsync(CancellationToken.None));
        Assert.Contains("LDAP 配置不完整", exception.Message);
    }

    [Fact]
    public async Task StartAsync_Incomplete_Oidc_Should_Refuse_To_Start()
    {
        using SqliteTestDatabase database = new();
        PlatformAuthOptions options = OptionsWithBootstrap();
        options.Oidc.Enabled = true;

        AuthDataInitializer initializer = CreateInitializer(
            database, options, HostingFakes.CreateDevelopmentEnvironment());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.StartAsync(CancellationToken.None));
        Assert.Contains("OIDC 配置不完整", exception.Message);
    }

    [Fact]
    public async Task StartAsync_Board_With_Wide_Scope_Should_Start_With_Warning_Only()
    {
        using SqliteTestDatabase database = new();
        PlatformAuthOptions options = OptionsWithBootstrap();
        options.Board.Enabled = true;
        options.Board.IpWhitelist = ["192.168.1.0/16"];

        AuthDataInitializer initializer = CreateInitializer(
            database, options, HostingFakes.CreateDevelopmentEnvironment());

        // 宽网段只告警不阻断：单机 /32 之外的条目由现场决策。
        Exception? exception = await Record.ExceptionAsync(() => initializer.StartAsync(CancellationToken.None));
        Assert.Null(exception);
    }

    [Fact]
    public async Task StopAsync_Should_Complete()
    {
        using SqliteTestDatabase database = new();
        AuthDataInitializer initializer = CreateInitializer(
            database, OptionsWithBootstrap(), HostingFakes.CreateDevelopmentEnvironment());

        await initializer.StopAsync(CancellationToken.None);
    }
}
