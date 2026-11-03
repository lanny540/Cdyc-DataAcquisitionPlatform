using DAP.Core.Shared.Catalog;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Layout;

/// <summary>
/// 左侧导航中的一个可点击条目。
/// </summary>
/// <param name="Title">菜单显示名称。</param>
/// <param name="Href">目标路由，根路由写 "/"。</param>
/// <param name="Icon">Material 图标常量，必须确认存在于 Icons.Material.Filled。</param>
/// <param name="Accent">导航图标取色类，取值来自 app.css 已定义的 platform-nav-link--* 修饰类。</param>
/// <param name="PageKey">页面稳定标识，落库后不随路由改动而变化，是启动对账的比对键。</param>
/// <param name="DomainCode">默认所属职能域。库中可覆盖，见 docs/AUTH_DESIGN.md 第 4 节。</param>
/// <param name="AccessLevel">页面等级：1 查看类，2 配置类。</param>
/// <param name="RequiredPermission">
/// 页面要求的权限点。为空表示只按域与等级判定；非空时导航与页面闸门都要求持有该权限点。
/// 部门管理员靠它看到「用户与角色」——权限点已在其角色里，不需要额外授予系统域。
/// </param>
/// <param name="IsShared">
/// 是否为跨域共享页。共享页不依赖域授权（部门管理员的可见域是业务域，不是平台管理域），
/// 改为「持有 RequiredPermission 即可见」，在导航里以「权限管理」合成域呈现。
/// </param>
public sealed record NavItem(
    string Title,
    string Href,
    string Icon,
    string Accent,
    string PageKey,
    string DomainCode = AuthorizationCatalog.BanknotePaperDomain,
    int AccessLevel = AuthorizationCatalog.ViewAccessLevel,
    string? RequiredPermission = null,
    bool IsShared = false)
{
    /// <summary>
    /// 根路由必须精确匹配：Prefix 匹配下 "/" 会命中所有页面，导致平台概览永远高亮。
    /// </summary>
    public bool RequiresExactMatch => Href is "" or "/";

    /// <summary>
    /// MudNavLink 使用的匹配方式。
    /// </summary>
    public NavLinkMatch Match => RequiresExactMatch ? NavLinkMatch.All : NavLinkMatch.Prefix;

    /// <summary>
    /// 是否为配置类页面。
    /// </summary>
    public bool IsConfigurationPage => AccessLevel >= AuthorizationCatalog.ConfigureAccessLevel;

    /// <summary>
    /// 是否要求额外的权限点。
    /// </summary>
    public bool RequiresPermission => !string.IsNullOrWhiteSpace(RequiredPermission);
}

/// <summary>
/// 表示左侧导航中的一个分组标题及其子菜单。
/// </summary>
/// <param name="Title">分组标题，例如「能耗指标」。</param>
/// <param name="Items">该分组下的菜单条目。</param>
public sealed record NavGroup(string Title, IReadOnlyList<NavItem> Items);

/// <summary>
/// 表示顶部导航栏的一个顶级模块。点击后左侧菜单只展示该模块的分组与子菜单。
/// </summary>
/// <param name="Id">模块标识，用于状态比对。</param>
/// <param name="Title">顶栏显示名称。</param>
/// <param name="Description">抽屉头部的一句话说明。</param>
/// <param name="Icon">顶栏图标。</param>
/// <param name="QuickLinks">不归属任何分组的直接条目。</param>
/// <param name="Groups">带分组标题的子菜单。</param>
public sealed record NavSection(
    string Id,
    string Title,
    string Description,
    string Icon,
    IReadOnlyList<NavItem> QuickLinks,
    IReadOnlyList<NavGroup> Groups)
{
    /// <summary>
    /// 模块内的全部条目，按「直接条目 → 各分组条目」的顺序展开。
    /// </summary>
    public IEnumerable<NavItem> AllItems => QuickLinks.Concat(Groups.SelectMany(group => group.Items));

    /// <summary>
    /// 模块的落地页：点击顶栏后若当前页面不属于该模块，跳转到此地址。
    /// </summary>
    public string LandingHref => AllItems.FirstOrDefault()?.Href ?? "/";
}

/// <summary>
/// 表示导航中的一个页面及其所在层级。启动对账据此写入域与页面归属。
/// </summary>
/// <param name="Item">页面条目。</param>
/// <param name="Section">所属顶级模块。</param>
/// <param name="GroupTitle">分组标题；直接挂在模块下的条目为空。</param>
public sealed record NavPageDescriptor(NavItem Item, NavSection Section, string? GroupTitle);

/// <summary>
/// 平台三级导航目录：顶级模块 → 分组 → 页面。
///
/// 顶栏与左侧菜单共用本目录，避免两处各写一份菜单导致层级与路由漂移。
/// 新增页面时在此登记即可，守卫测试会校验每个 href 都有对应的 @page 路由。
/// </summary>
public static class NavigationCatalog
{
    /// <summary>数据指标模块标识。</summary>
    public const string DataMetrics = "data-metrics";

    /// <summary>业务配置模块标识。</summary>
    public const string BusinessConfig = "business-config";

    /// <summary>平台管理模块标识。</summary>
    public const string PlatformAdmin = "platform-admin";

    private const string AccentPrimary = "platform-nav-link--primary";
    private const string AccentTertiary = "platform-nav-link--tertiary";
    private const string AccentSuccess = "platform-nav-link--success";
    private const string AccentWarning = "platform-nav-link--warning";
    private const string AccentInfo = "platform-nav-link--info";

    private static readonly NavSection[] AllSections =
    [
        new(
            DataMetrics,
            "数据指标",
            "能耗指标与数据报表",
            Icons.Material.Filled.Insights,
            [],
            [
                new NavGroup("能耗指标",
                [
                    // 根路由交给能耗仪表板：启动应用即落在「数据指标」，而不是平台管理。
                    new NavItem("能耗仪表板", "/", Icons.Material.Filled.QueryStats, AccentSuccess,
                        "energy-dashboard"),
                    new NavItem("历史指标", "/energy-history", Icons.Material.Filled.Timeline, AccentTertiary,
                        "energy-history"),
                    new NavItem("班组横向对比", "/energy-comparison", Icons.Material.Filled.Leaderboard, AccentWarning,
                        "energy-comparison")
                ]),
                new NavGroup("数据报表",
                [
                    new NavItem("班组能耗报表", "/energy-report", Icons.Material.Filled.Assessment, AccentPrimary,
                        "energy-report")
                ])
            ]),
        new(
            BusinessConfig,
            "业务配置",
            "班组与排班配置",
            Icons.Material.Filled.Tune,
            [],
            [
                new NavGroup("班组管理",
                [
                    new NavItem("排班时间管理", "/shift-schedules", Icons.Material.Filled.UploadFile, AccentPrimary,
                        "shift-schedules", AccessLevel: AuthorizationCatalog.ConfigureAccessLevel),
                    new NavItem("排班日历", "/shift-calendar", Icons.Material.Filled.CalendarMonth, AccentInfo,
                        "shift-calendar", AccessLevel: AuthorizationCatalog.ConfigureAccessLevel)
                ])
            ]),
        new(
            PlatformAdmin,
            "平台管理",
            "采集配置与接口联调",
            Icons.Material.Filled.AdminPanelSettings,
            [
                new NavItem("平台概览", "/platform-overview", Icons.Material.Filled.Dashboard, AccentPrimary,
                    "platform-overview", AuthorizationCatalog.PlatformDomain),
                new NavItem("服务器状态查询", "/server-connection-status", Icons.Material.Filled.Router, AccentInfo,
                    "server-connection-status", AuthorizationCatalog.PlatformDomain)
            ],
            [
                new NavGroup("采集配置",
                [
                    new NavItem("点位配置", "/collection-points", Icons.Material.Filled.SettingsInputComponent,
                        AccentInfo, "collection-points", AuthorizationCatalog.PlatformDomain,
                        AuthorizationCatalog.ConfigureAccessLevel),
                    new NavItem("点位数据管理", "/managed-data-definitions", Icons.Material.Filled.Storage,
                        AccentTertiary, "managed-data-definitions", AuthorizationCatalog.PlatformDomain,
                        AuthorizationCatalog.ConfigureAccessLevel)
                ]),
                new NavGroup("接口管理",
                [
                    new NavItem("History API 测试", "/history-api-test", Icons.Material.Filled.Api, AccentWarning,
                        "history-api-test", AuthorizationCatalog.PlatformDomain,
                        AuthorizationCatalog.ConfigureAccessLevel),
                    new NavItem("MQTT 测试", "/mqtt-test", Icons.Material.Filled.Sensors, AccentTertiary,
                        "mqtt-test", AuthorizationCatalog.PlatformDomain,
                        AuthorizationCatalog.ConfigureAccessLevel)
                ]),
                new NavGroup("权限管理",
                [
                    new NavItem("用户与角色", "/user-management", Icons.Material.Filled.ManageAccounts,
                        AccentInfo, "user-management", AuthorizationCatalog.PlatformDomain,
                        AuthorizationCatalog.ConfigureAccessLevel,
                        AuthorizationCatalog.UserManagePermission, IsShared: true),
                    new NavItem("域与页面配置", "/domain-config", Icons.Material.Filled.AccountTree, AccentPrimary,
                        "domain-config", AuthorizationCatalog.PlatformDomain,
                        AuthorizationCatalog.ConfigureAccessLevel),
                    new NavItem("部门与授权", "/department-grants", Icons.Material.Filled.Group, AccentSuccess,
                        "department-grants", AuthorizationCatalog.PlatformDomain,
                        AuthorizationCatalog.ConfigureAccessLevel),
                    new NavItem("系统审计", "/system-audit", Icons.Material.Filled.FactCheck, AccentWarning,
                        "system-audit", AuthorizationCatalog.PlatformDomain,
                        AuthorizationCatalog.ConfigureAccessLevel,
                        AuthorizationCatalog.SystemManagePermission)
                ])
            ])
    ];

    /// <summary>
    /// 域的顶栏图标。域图标不落库（MudBlazor 图标常量是整段 SVG path），
    /// 新增域时在此登记一枚图标即可，未登记的域用默认图标。
    /// </summary>
    /// <param name="domainCode">域编码。</param>
    /// <returns>图标常量值。</returns>
    public static string DomainIcon(string domainCode)
    {
        return domainCode switch
        {
            AuthorizationCatalog.BanknotePaperDomain => Icons.Material.Filled.QueryStats,
            AuthorizationCatalog.BanknotePrintingDomain => Icons.Material.Filled.Assessment,
            AuthorizationCatalog.EnergyDomain => Icons.Material.Filled.Insights,
            AuthorizationCatalog.PlatformDomain => Icons.Material.Filled.AdminPanelSettings,
            _ => Icons.Material.Filled.Dashboard
        };
    }

    /// <summary>
    /// 全部顶级模块，顺序即顶栏顺序。
    /// </summary>
    public static IReadOnlyList<NavSection> Sections => AllSections;

    /// <summary>
    /// 全部页面的扁平视图，含所属模块与分组标题。启动对账用它写入 domain_pages。
    /// </summary>
    public static IReadOnlyList<NavPageDescriptor> Pages => AllPages;

    /// <summary>
    /// 全部跨域共享页（<see cref="NavItem.IsShared"/>）。共享页不依赖域授权，
    /// 改为「持有 <see cref="NavItem.RequiredPermission"/> 即可见」。
    /// 用 Lazy 延迟到类型初始化完成后取数，避免与 <c>AllPages</c> 的静态初始化顺序耦合。
    /// </summary>
    public static IReadOnlyList<NavItem> SharedPages => SharedPagesCache.Value;

    private static readonly Lazy<NavItem[]> SharedPagesCache =
        new(() => [.. Pages.Where(page => page.Item.IsShared).Select(page => page.Item)]);

    /// <summary>
    /// 按路由反查共享页。采用最长前缀匹配，与页面闸门的匹配规则一致；
    /// 非共享页或未登记路径返回 null。
    /// </summary>
    /// <param name="relativePath">相对基础路径的地址，例如 user-management。</param>
    /// <returns>命中的共享页条目；未命中返回 null。</returns>
    public static NavItem? ResolveSharedPage(string? relativePath)
    {
        var path = Normalize(relativePath);

        NavItem? matched = null;
        var matchedLength = -1;

        foreach (NavItem item in SharedPages)
        {
            var href = Normalize(item.Href);

            var isMatch = href.Length == 0
                ? path.Length == 0
                : path.Equals(href, StringComparison.OrdinalIgnoreCase)
                  || path.StartsWith(href + "/", StringComparison.OrdinalIgnoreCase);

            if (!isMatch || href.Length <= matchedLength)
            {
                continue;
            }

            matched = item;
            matchedLength = href.Length;
        }

        return matched;
    }

    private static readonly NavPageDescriptor[] AllPages = BuildPages();

    private static NavPageDescriptor[] BuildPages()
    {
        List<NavPageDescriptor> pages = [];

        foreach (NavSection section in AllSections)
        {
            foreach (NavItem item in section.QuickLinks)
            {
                pages.Add(new NavPageDescriptor(item, section, null));
            }

            foreach (NavGroup group in section.Groups)
            {
                foreach (NavItem item in group.Items)
                {
                    pages.Add(new NavPageDescriptor(item, section, group.Title));
                }
            }
        }

        return pages.ToArray();
    }

    /// <summary>
    /// 默认模块为「数据指标」：应用根路由（/）即其落地页「能耗仪表板」，
    /// 因此启动后顶栏默认选中它；未知路由同样回落到这里。
    /// </summary>
    public static NavSection DefaultSection => AllSections[0];

    /// <summary>
    /// 按模块标识取模块，标识不存在时返回默认模块。
    /// </summary>
    /// <param name="id">模块标识。</param>
    /// <returns>匹配的模块。</returns>
    public static NavSection Find(string? id)
    {
        return AllSections.FirstOrDefault(section => string.Equals(section.Id, id, StringComparison.Ordinal))
               ?? DefaultSection;
    }

    /// <summary>
    /// 按当前路由反查所属模块，用于刷新页面或直接输入地址时同步顶栏高亮。
    ///
    /// 采用最长前缀匹配：/managed-data-definitions/{id} 会归到「点位数据管理」，
    /// 而不是被更短的条目抢先命中。
    /// </summary>
    /// <param name="relativePath">相对基础路径的地址，例如 energy-dashboard。</param>
    /// <returns>所属模块。</returns>
    public static NavSection ResolveSection(string? relativePath)
    {
        var path = Normalize(relativePath);

        NavSection? matched = null;
        var matchedLength = -1;

        foreach (NavSection section in AllSections)
        {
            foreach (NavItem item in section.AllItems)
            {
                var href = Normalize(item.Href);

                var isMatch = href.Length == 0
                    ? path.Length == 0
                    : path.Equals(href, StringComparison.OrdinalIgnoreCase)
                      || path.StartsWith(href + "/", StringComparison.OrdinalIgnoreCase);

                if (!isMatch || href.Length <= matchedLength)
                {
                    continue;
                }

                matched = section;
                matchedLength = href.Length;
            }
        }

        return matched ?? DefaultSection;
    }

    /// <summary>
    /// 判断给定路由是否属于指定模块。
    /// </summary>
    /// <param name="section">模块。</param>
    /// <param name="relativePath">相对基础路径的地址。</param>
    /// <returns>属于该模块时返回 true。</returns>
    public static bool Contains(NavSection section, string? relativePath)
    {
        return string.Equals(ResolveSection(relativePath).Id, section.Id, StringComparison.Ordinal);
    }

    /// <summary>
    /// 归一化地址：去掉首尾斜杠与查询串，便于统一比较。
    /// </summary>
    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim();

        var queryIndex = trimmed.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
        {
            trimmed = trimmed[..queryIndex];
        }

        return trimmed.Trim('/');
    }
}
