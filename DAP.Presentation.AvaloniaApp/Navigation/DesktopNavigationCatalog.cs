using DAP.Presentation.AvaloniaApp.ViewModels;
using DAP.Presentation.AvaloniaApp.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace DAP.Presentation.AvaloniaApp.Navigation;

/// <summary>
/// 左侧导航中的一个页面条目。
/// </summary>
/// <param name="PageKey">页面稳定标识，见 <see cref="PageKeys"/>。</param>
/// <param name="Title">菜单显示名称。</param>
/// <param name="Caption">子项说明，显示在抽屉与页面副标题中。</param>
/// <param name="Icon">Material 图标路径数据。</param>
/// <param name="ViewModelType">页面视图模型类型，由页面工厂按需创建。</param>
public sealed record DesktopNavItem(
    string PageKey,
    string Title,
    string Caption,
    string Icon,
    Type ViewModelType,
    string Accent);

/// <summary>
/// 导航分组。
/// </summary>
/// <param name="Title">分组标题。</param>
/// <param name="Items">分组内页面。</param>
public sealed record DesktopNavGroup(string Title, IReadOnlyList<DesktopNavItem> Items);

/// <summary>
/// 顶级模块。桌面端只保留「数据指标」与「数据查看」两域，共 6 个页面：
/// 数据查看只留实时采集数据与排班日历——采集点、后台节点、采集服务器这类配置入口
/// 现场很少用，且与 Web 端重复，不如把配置全部收敛到 Web，客户端保持轻量。
/// </summary>
/// <param name="Id">模块标识。</param>
/// <param name="Title">顶栏显示名称。</param>
/// <param name="Description">抽屉头部说明。</param>
/// <param name="Groups">分组与页面。</param>
public sealed record DesktopNavModule(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<DesktopNavGroup> Groups)
{
    /// <summary>
    /// 模块内全部页面，按分组顺序展开。
    /// </summary>
    public IEnumerable<DesktopNavItem> AllItems => Groups.SelectMany(group => group.Items);

    /// <summary>
    /// 模块落地页。
    /// </summary>
    public DesktopNavItem LandingItem => AllItems.First();
}

/// <summary>
/// 桌面端导航目录。
///
/// 与 Web 端 <c>NavigationCatalog</c> 同构（模块 → 分组 → 页面），
/// 但不含路由、权限与 MudBlazor 图标常量，因此各自维护一份而不是强行共用：
/// 强行共用会把 Web 的路由概念带进桌面端，反过来又要把桌面端专属页面塞进 Web 菜单。
/// </summary>
public static class DesktopNavigationCatalog
{
    /// <summary>数据指标模块。</summary>
    public const string DataMetricsModule = "data-metrics";

    /// <summary>数据查看模块。</summary>
    public const string DataViewModule = "data-view";

    /// <summary>全部模块，顺序即顶栏顺序。</summary>
    public static IReadOnlyList<DesktopNavModule> Modules { get; } =
    [
        new(
            DataMetricsModule,
            "数据指标",
            "能耗指标与班组报表，与 Web 端同源同算。",
            [
                new DesktopNavGroup("能耗指标",
                [
                    new DesktopNavItem(PageKeys.EnergyDashboard, "能耗仪表板", "当前班组 4 项指标与趋势",
                        NavIcons.Dashboard, typeof(EnergyDashboardPageViewModel), "success"),
                    new DesktopNavItem(PageKeys.EnergyHistory, "历史指标", "按日、周、月查看单项趋势",
                        NavIcons.Timeline, typeof(EnergyHistoryPageViewModel), "tertiary"),
                    new DesktopNavItem(PageKeys.EnergyComparison, "班组横向对比", "同口径下各班组消耗对比",
                        NavIcons.BarChart, typeof(EnergyComparisonPageViewModel), "warning")
                ]),
                new DesktopNavGroup("数据报表",
                [
                    new DesktopNavItem(PageKeys.EnergyReport, "班组能耗报表", "区间汇总、明细与导出",
                        NavIcons.Assessment, typeof(EnergyReportPageViewModel), "secondary")
                ])
            ]),
        new(
            DataViewModule,
            "数据查看",
            "实时采集数据与班组排班，全部只读。",
            [
                new DesktopNavGroup("采集运行",
                [
                    new DesktopNavItem(PageKeys.CollectionData, "实时采集数据", "最新入库的采集记录",
                        NavIcons.Sensors, typeof(CollectionDataPageViewModel), "info")
                ]),
                new DesktopNavGroup("班组排班",
                [
                    new DesktopNavItem(PageKeys.ShiftCalendar, "排班日历", "每日在岗与轮休班组",
                        NavIcons.Calendar, typeof(ShiftCalendarPageViewModel), "info")
                ])
            ])
    ];

    /// <summary>
    /// 默认模块（应用启动后落在「数据指标 → 能耗仪表板」）。
    /// </summary>
    public static DesktopNavModule DefaultModule => Modules[0];

    /// <summary>
    /// 按页面标识查找条目。
    /// </summary>
    /// <param name="pageKey">页面标识。</param>
    /// <returns>命中的条目；未命中返回 null。</returns>
    public static DesktopNavItem? FindItem(string? pageKey)
    {
        if (string.IsNullOrWhiteSpace(pageKey))
        {
            return null;
        }

        return Modules
            .SelectMany(module => module.AllItems)
            .FirstOrDefault(item => string.Equals(item.PageKey, pageKey, StringComparison.Ordinal));
    }

    /// <summary>
    /// 按页面标识反查所属模块。
    /// </summary>
    /// <param name="pageKey">页面标识。</param>
    /// <returns>所属模块；未命中返回默认模块。</returns>
    public static DesktopNavModule ResolveModule(string? pageKey)
    {
        return Modules.FirstOrDefault(module =>
                   module.AllItems.Any(item => string.Equals(item.PageKey, pageKey, StringComparison.Ordinal)))
               ?? DefaultModule;
    }
}

/// <summary>
/// 页面工厂：按导航条目创建页面视图模型，并缓存实例。
///
/// 缓存的意义不止省内存：切换模块再切回来时，筛选条件与已加载数据都还在，
/// 不会每次都重新打一轮接口；需要最新数据时页面自己有刷新按钮。
/// </summary>
public sealed class PageFactory(IServiceProvider services)
{
    private readonly Dictionary<string, PageViewModelBase> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// 取得页面视图模型。
    /// </summary>
    /// <param name="item">导航条目。</param>
    /// <returns>页面视图模型。</returns>
    public PageViewModelBase Resolve(DesktopNavItem item)
    {
        if (_cache.TryGetValue(item.PageKey, out PageViewModelBase? cached))
        {
            return cached;
        }

        PageViewModelBase page = (PageViewModelBase)services.GetRequiredService(item.ViewModelType);
        _cache[item.PageKey] = page;
        return page;
    }
}
