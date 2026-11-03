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
public sealed record NavItem(string Title, string Href, string Icon, string Accent)
{
    /// <summary>
    /// 根路由必须精确匹配：Prefix 匹配下 "/" 会命中所有页面，导致平台概览永远高亮。
    /// </summary>
    public bool RequiresExactMatch => Href is "" or "/";

    /// <summary>
    /// MudNavLink 使用的匹配方式。
    /// </summary>
    public NavLinkMatch Match => RequiresExactMatch ? NavLinkMatch.All : NavLinkMatch.Prefix;
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
                    new NavItem("能耗仪表板", "/", Icons.Material.Filled.QueryStats, AccentSuccess),
                    new NavItem("历史指标", "/energy-history", Icons.Material.Filled.Timeline, AccentTertiary),
                    new NavItem("班组横向对比", "/energy-comparison", Icons.Material.Filled.Leaderboard, AccentWarning)
                ]),
                new NavGroup("数据报表",
                [
                    new NavItem("班组能耗报表", "/energy-report", Icons.Material.Filled.Assessment, AccentPrimary)
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
                    new NavItem("排班时间管理", "/shift-schedules", Icons.Material.Filled.UploadFile, AccentPrimary),
                    new NavItem("排班日历", "/shift-calendar", Icons.Material.Filled.CalendarMonth, AccentInfo)
                ])
            ]),
        new(
            PlatformAdmin,
            "平台管理",
            "采集配置与接口联调",
            Icons.Material.Filled.AdminPanelSettings,
            [
                new NavItem("平台概览", "/platform-overview", Icons.Material.Filled.Dashboard, AccentPrimary),
                new NavItem("服务器状态查询", "/server-connection-status", Icons.Material.Filled.Router, AccentInfo)
            ],
            [
                new NavGroup("采集配置",
                [
                    new NavItem("点位配置", "/collection-points", Icons.Material.Filled.SettingsInputComponent,
                        AccentInfo),
                    new NavItem("点位数据管理", "/managed-data-definitions", Icons.Material.Filled.Storage,
                        AccentTertiary)
                ]),
                new NavGroup("接口管理",
                [
                    new NavItem("History API 测试", "/history-api-test", Icons.Material.Filled.Api, AccentWarning),
                    new NavItem("MQTT 测试", "/mqtt-test", Icons.Material.Filled.Sensors, AccentTertiary)
                ])
            ])
    ];

    /// <summary>
    /// 全部顶级模块，顺序即顶栏顺序。
    /// </summary>
    public static IReadOnlyList<NavSection> Sections => AllSections;

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
