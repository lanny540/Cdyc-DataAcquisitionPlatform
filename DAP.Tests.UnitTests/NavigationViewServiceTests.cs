using System.Security.Claims;
using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Tests.UnitTests.TestDoubles;
using MudBlazor;

namespace DAP.Tests.UnitTests;

/// <summary>
/// <see cref="NavigationViewService"/> 的导航树构建测试。
///
/// 顶栏渲染的是「用户可见域列表」而不是固定模块：域可见性按 claims 过滤、
/// 页面按等级过滤、空域对普通用户隐藏、对超管保留并标记「未上线」。
/// 这里逐条验证这些服务端过滤规则——导航过滤写错等于授权矩阵旁路。
/// </summary>
public class NavigationViewServiceTests
{
    private static readonly DateTimeOffset SeedTime = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    /// <summary>超管主体：域与页面等级均短路。</summary>
    private static ClaimsPrincipal SuperAdmin()
    {
        return Principal([], AuthorizationCatalog.SuperAdminPageLevel, [], isSuperAdmin: true);
    }

    /// <summary>普通用户主体。</summary>
    private static ClaimsPrincipal Principal(
        IReadOnlyList<string> domains,
        int pageLevel,
        IReadOnlyList<string> permissions,
        bool isSuperAdmin = false)
    {
        AuthUserDto user = new(
            Guid.NewGuid(),
            "tester",
            "测试用户",
            "测试部门",
            ["viewer"],
            domains,
            pageLevel,
            permissions,
            MustChangePassword: false,
            IsSuperAdmin: isSuperAdmin);

        return PlatformClaimsFactory.CreatePrincipal(user, SeedTime);
    }

    private static BusinessDomain Domain(
        string code,
        string name,
        int sortOrder,
        bool isSystem = false,
        bool isEnabled = true,
        bool visibleWhenEmpty = false)
    {
        return new BusinessDomain
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            SortOrder = sortOrder,
            IsSystem = isSystem,
            IsEnabled = isEnabled,
            VisibleWhenEmpty = visibleWhenEmpty,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime
        };
    }

    private static DomainPage Page(
        Guid domainId,
        string pageKey,
        string title,
        int sortOrder,
        int accessLevel = 1,
        string? groupTitle = null,
        bool isEnabled = true,
        bool isOrphaned = false,
        string? accent = null)
    {
        return new DomainPage
        {
            Id = Guid.NewGuid(),
            PageKey = pageKey,
            DomainId = domainId,
            Route = $"/{pageKey}",
            Title = title,
            GroupTitle = groupTitle,
            Accent = accent,
            AccessLevel = accessLevel,
            SortOrder = sortOrder,
            IsEnabled = isEnabled,
            IsOrphaned = isOrphaned,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime
        };
    }

    [Fact]
    public async Task 超管可见全部启用域_含系统域与空域_且不追加共享域()
    {
        using SqliteTestDatabase database = new();
        BusinessDomain alpha = Domain("alpha", "甲域", 10);
        BusinessDomain system = Domain("sys", "系统域", 20, isSystem: true);
        BusinessDomain empty = Domain("empty", "空域", 30);
        BusinessDomain disabled = Domain("off", "停用域", 5, isEnabled: false);
        Seed(database, [alpha, system, empty, disabled],
            [Page(alpha.Id, "p1", "页面一", 10)]);

        NavigationViewService service = new(HostingFakes.CreateScopeFactory(database));

        NavigationDto navigation = await service.BuildAsync(SuperAdmin());

        // 停用域对超管同样不可见；空域保留并带「未上线」标记；系统域可见。
        Assert.Equal(["alpha", "sys", "empty"],
            navigation.Domains.Select(domain => domain.Code).ToArray());
        Assert.Contains(navigation.Domains, domain => domain.Code == "sys" && domain.IsSystem);
        Assert.Contains(navigation.Domains, domain => domain.Code == "empty" && domain.IsEmpty);
        // 超管已在系统域里看到共享页，无需再合成「权限管理」域。
        Assert.DoesNotContain(navigation.Domains, domain => domain.Code == NavigationViewService.SharedDomainCode);
    }

    [Fact]
    public async Task 普通用户只见已授权业务域_系统域与未授权域被过滤()
    {
        using SqliteTestDatabase database = new();
        BusinessDomain alpha = Domain("alpha", "甲域", 10);
        BusinessDomain beta = Domain("beta", "乙域", 20);
        BusinessDomain system = Domain("sys", "系统域", 30, isSystem: true);
        Seed(database, [alpha, beta, system],
            [Page(alpha.Id, "p1", "页面一", 10), Page(beta.Id, "p2", "页面二", 10)]);

        NavigationViewService service = new(HostingFakes.CreateScopeFactory(database));

        NavigationDto navigation = await service.BuildAsync(Principal(["alpha"], 1, []));

        Assert.Equal(["alpha"], navigation.Domains.Select(domain => domain.Code).ToArray());
    }

    [Fact]
    public async Task 页面按用户等级过滤_等级一不见配置页()
    {
        using SqliteTestDatabase database = new();
        BusinessDomain beta = Domain("beta", "乙域", 10);
        DomainPage view = Page(beta.Id, "view", "查看页", 10, accessLevel: 1);
        DomainPage config = Page(beta.Id, "config", "配置页", 20, accessLevel: 2);
        Seed(database, [beta], [view, config]);

        NavigationViewService service = new(HostingFakes.CreateScopeFactory(database));

        NavigationDto level1 = await service.BuildAsync(Principal(["beta"], 1, []));
        NavigationDto level2 = await service.BuildAsync(Principal(["beta"], 2, []));

        // 车间只读终端（等级 1）只见查看类页面，配置类页面连菜单都不出现。
        Assert.Equal(["view"], level1.Domains[0].Groups[0].Pages.Select(page => page.PageKey).ToArray());
        Assert.Equal(["view", "config"],
            level2.Domains[0].Groups[0].Pages.Select(page => page.PageKey).ToArray());
    }

    [Fact]
    public async Task 停用页面与孤儿页面被过滤()
    {
        using SqliteTestDatabase database = new();
        BusinessDomain alpha = Domain("alpha", "甲域", 10);
        Seed(database, [alpha],
        [
            Page(alpha.Id, "ok", "正常页", 10),
            Page(alpha.Id, "off", "停用页", 20, isEnabled: false),
            Page(alpha.Id, "gone", "下线页", 30, isOrphaned: true)
        ]);

        NavigationViewService service = new(HostingFakes.CreateScopeFactory(database));

        NavigationDto navigation = await service.BuildAsync(Principal(["alpha"], 1, []));

        Assert.Equal(["ok"], navigation.Domains[0].Groups[0].Pages.Select(page => page.PageKey).ToArray());
    }

    [Fact]
    public async Task 空域对普通用户隐藏_VisibleWhenEmpty打开后保留()
    {
        using SqliteTestDatabase database = new();
        BusinessDomain hidden = Domain("hidden", "空域", 10);
        BusinessDomain shown = Domain("shown", "空域但可见", 20, visibleWhenEmpty: true);
        Seed(database, [hidden, shown], []);

        NavigationViewService service = new(HostingFakes.CreateScopeFactory(database));

        NavigationDto navigation = await service.BuildAsync(Principal(["hidden", "shown"], 1, []));

        // 默认关闭避免用户点进空白板块；打开后域保留但标记为空。
        Assert.Equal(["shown"], navigation.Domains.Select(domain => domain.Code).ToArray());
        Assert.True(navigation.Domains[0].IsEmpty);
        Assert.Empty(navigation.Domains[0].Groups);
        Assert.Null(navigation.Domains[0].FirstPage);
    }

    [Fact]
    public async Task 分组顺序_直接条目在最前_分组按首次出现顺序排列()
    {
        using SqliteTestDatabase database = new();
        BusinessDomain alpha = Domain("alpha", "甲域", 10);
        Seed(database, [alpha],
        [
            Page(alpha.Id, "d", "丁页", 5, groupTitle: "G2"),
            Page(alpha.Id, "b", "乙页", 10, groupTitle: "G1"),
            Page(alpha.Id, "c", "丙页", 20),
            Page(alpha.Id, "a", "甲页", 30),
            Page(alpha.Id, "e", "戊页", 40, groupTitle: "G1")
        ]);

        NavigationViewService service = new(HostingFakes.CreateScopeFactory(database));

        NavigationDto navigation = await service.BuildAsync(Principal(["alpha"], 1, []));

        IReadOnlyList<NavigationGroupDto> groups = navigation.Domains[0].Groups;
        // 直接挂在域下的条目（GroupTitle 为空）排最前，渲染时不显示分组标题。
        Assert.Equal(3, groups.Count);
        Assert.Equal(string.Empty, groups[0].Title);
        Assert.Equal(["c", "a"], groups[0].Pages.Select(page => page.PageKey).ToArray());
        // 带分组标题的页面按页面排序中首次出现的顺序分组。
        Assert.Equal("G2", groups[1].Title);
        Assert.Equal(["d"], groups[1].Pages.Select(page => page.PageKey).ToArray());
        Assert.Equal("G1", groups[2].Title);
        Assert.Equal(["b", "e"], groups[2].Pages.Select(page => page.PageKey).ToArray());
    }

    [Fact]
    public async Task 图标按代码常量补充_未登记页面回退默认图标()
    {
        using SqliteTestDatabase database = new();
        BusinessDomain alpha = Domain("alpha", "甲域", 10);
        Seed(database, [alpha],
        [
            Page(alpha.Id, "energy-dashboard", "能耗仪表板", 10),
            Page(alpha.Id, "custom-unknown", "未登记页", 20)
        ]);

        NavigationViewService service = new(HostingFakes.CreateScopeFactory(database));

        NavigationDto navigation = await service.BuildAsync(Principal(["alpha"], 1, []));

        IReadOnlyList<NavigationPageDto> pages = navigation.Domains[0].Groups[0].Pages;
        Assert.Equal(Icons.Material.Filled.QueryStats, pages[0].Icon);
        Assert.Equal(Icons.Material.Filled.Dashboard, pages[1].Icon);
        // Accent 未配置时回退默认修饰类。
        Assert.Equal("platform-nav-link--primary", pages[0].Accent);
    }

    [Fact]
    public async Task 持有权限点的普通用户获得共享页合成域()
    {
        using SqliteTestDatabase database = new();
        // 共享页不依赖域授权：用户没有任何业务域授权，仅持有用户管理权限点。
        Seed(database, [Domain("alpha", "甲域", 10)], []);

        NavigationViewService service = new(HostingFakes.CreateScopeFactory(database));

        NavigationDto navigation = await service.BuildAsync(
            Principal([], 2, [AuthorizationCatalog.UserManagePermission]));

        NavigationDomainDto? shared = navigation.Domains.SingleOrDefault(
            domain => domain.Code == NavigationViewService.SharedDomainCode);

        Assert.NotNull(shared);
        Assert.Equal("权限管理", shared.Name);
        Assert.False(shared.IsSystem);
        Assert.False(shared.IsEmpty);

        NavigationPageDto page = Assert.Single(shared.Groups[0].Pages);
        Assert.Equal("user-management", page.PageKey);
        Assert.Equal("用户与角色", page.Title);
        Assert.Equal("/user-management", page.Route);
    }

    [Fact]
    public async Task 共享页对缺少权限点或等级不足的用户不可见()
    {
        using SqliteTestDatabase database = new();
        Seed(database, [Domain("alpha", "甲域", 10)], []);

        NavigationViewService service = new(HostingFakes.CreateScopeFactory(database));

        // 等级够但没有权限点。
        NavigationDto noPermission = await service.BuildAsync(Principal([], 2, []));
        // 持有权限点但等级只有 1（共享页是配置类，AccessLevel = 2）。
        NavigationDto lowLevel = await service.BuildAsync(
            Principal([], 1, [AuthorizationCatalog.UserManagePermission]));

        Assert.DoesNotContain(noPermission.Domains,
            domain => domain.Code == NavigationViewService.SharedDomainCode);
        Assert.DoesNotContain(lowLevel.Domains,
            domain => domain.Code == NavigationViewService.SharedDomainCode);
    }

    [Fact]
    public async Task 未认证会话可见域为空_且无共享域()
    {
        using SqliteTestDatabase database = new();
        BusinessDomain alpha = Domain("alpha", "甲域", 10);
        Seed(database, [alpha], [Page(alpha.Id, "p1", "页面一", 10)]);

        NavigationViewService service = new(HostingFakes.CreateScopeFactory(database));

        // 无 PageLevel claim：按 0 处理，任何页面都不可见。
        ClaimsPrincipal anonymous = new(new ClaimsIdentity());

        NavigationDto navigation = await service.BuildAsync(anonymous);

        Assert.Empty(navigation.Domains);
    }

    /// <summary>写入域与页面种子数据。</summary>
    private static void Seed(
        SqliteTestDatabase database,
        IReadOnlyList<BusinessDomain> domains,
        IReadOnlyList<DomainPage> pages)
    {
        using DataAcquisitionPlatformDbContext db = database.CreateContext();
        db.BusinessDomains.AddRange(domains);
        db.DomainPages.AddRange(pages);
        db.SaveChanges();
    }
}
