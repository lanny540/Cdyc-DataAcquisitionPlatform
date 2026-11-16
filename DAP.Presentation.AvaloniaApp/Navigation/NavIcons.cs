namespace DAP.Presentation.AvaloniaApp.Navigation;

/// <summary>
/// 页面稳定标识。
///
/// 与 Web 端的 <c>PageKey</c> 保持同名，便于排查「同一个页面两端显示不一致」，
/// 但桌面端不参与域授权与页面等级，因此不复用 Web 的导航目录（它绑定了 MudBlazor 图标与路由）。
/// </summary>
public static class PageKeys
{
    /// <summary>能耗仪表板。</summary>
    public const string EnergyDashboard = "energy-dashboard";

    /// <summary>历史指标。</summary>
    public const string EnergyHistory = "energy-history";

    /// <summary>班组横向对比。</summary>
    public const string EnergyComparison = "energy-comparison";

    /// <summary>班组能耗报表。</summary>
    public const string EnergyReport = "energy-report";

    /// <summary>实时采集数据。</summary>
    public const string CollectionData = "collection-data";

    /// <summary>排班日历。</summary>
    public const string ShiftCalendar = "shift-calendar";
}

/// <summary>
/// 导航图标（Material Design 路径数据）。图标与页面一一对应，禁止用序号取色或取图标。
/// </summary>
public static class NavIcons
{
    /// <summary>能耗仪表板。</summary>
    public const string Dashboard =
        "M3 13h8V3H3v10zm0 8h8v-6H3v6zm10 0h8V11h-8v10zm0-18v6h8V3h-8z";

    /// <summary>趋势/历史。</summary>
    public const string Timeline =
        "M3.5 18.49l6-6.01 4 4L22 6.92l-1.41-1.41-7.09 7.97-4-4L2 16.99z";

    /// <summary>横向对比。</summary>
    public const string BarChart =
        "M5 9.2h3V19H5V9.2zM10.6 5h2.8v14h-2.8V5zm5.6 8H19v6h-2.8v-6z";

    /// <summary>报表。</summary>
    public const string Assessment =
        "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zM9 17H7v-7h2v7zm4 0h-2V7h2v10zm4 0h-2v-4h2v4z";

    /// <summary>实时数据。</summary>
    public const string Sensors =
        "M7.76 16.24C6.67 15.16 6 13.66 6 12s.67-3.16 1.76-4.24l1.42 1.42C8.45 9.9 8 10.9 8 12s.45 2.1 1.18 2.82l-1.42 1.42zM16.24 16.24l-1.42-1.42C15.55 14.1 16 13.1 16 12s-.45-2.1-1.18-2.82l1.42-1.42C17.33 8.84 18 10.34 18 12s-.67 3.16-1.76 4.24zM12 10c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z";

    /// <summary>日历。</summary>
    public const string Calendar =
        "M19 3h-1V1h-2v2H8V1H6v2H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 16H5V8h14v11z";

    /// <summary>数据指标模块。</summary>
    public const string Insights =
        "M21 8c-1.45 0-2.26 1.44-1.93 2.51l-3.55 3.56c-.3-.09-.74-.09-1.04 0l-2.55-2.55C12.27 10.45 11.46 9 10 9c-1.45 0-2.27 1.44-1.93 2.52l-4.56 4.55C2.44 15.74 1 16.55 1 18c0 1.1.9 2 2 2 1.45 0 2.26-1.44 1.93-2.51l4.55-4.56c.3.09.74.09 1.04 0l2.55 2.55C12.73 16.55 13.54 18 15 18c1.45 0 2.27-1.44 1.93-2.52l3.56-3.55C21.56 12.26 23 11.45 23 10c0-1.1-.9-2-2-2z";

    /// <summary>化工/原辅材料指标。</summary>
    public const string Science =
        "M19.8 18.4L14 10.67V6.5l1.35-1.69c.26-.33.03-.81-.39-.81H9.04c-.42 0-.65.48-.39.81L10 6.5v4.17L4.2 18.4c-.49.66-.02 1.6.8 1.6h14c.82 0 1.29-.94.8-1.6z";

    /// <summary>水耗指标。</summary>
    public const string Opacity =
        "M17.66 8L12 2.35 6.34 8C4.78 9.56 4 11.64 4 13.64s.78 4.08 2.34 5.64C7.9 20.84 9.98 21.64 12 21.64s4.1-.79 5.66-2.35c1.56-1.56 2.34-3.64 2.34-5.64S19.22 9.56 17.66 8z";

    /// <summary>辅料指标。</summary>
    public const string Grain =
        "M4 8h4V4H4v4zm6 12h4v-4h-4v4zm-6 0h4v-4H4v4zm0-6h4v-4H4v4zm6 0h4v-4h-4v4zm6-10v4h4V4h-4zm-6 4h4V4h-4v4zm6 6h4v-4h-4v4zm0 6h4v-4h-4v4z";

    /// <summary>电耗指标。</summary>
    public const string Bolt =
        "M11 21h-1l1-7H7.5c-.58 0-.57-.32-.38-.66.19-.34.05-.08.07-.12C8.48 10.94 10.42 7.54 13 3h1l-1 7h3.5c.49 0 .56.33.47.51l-.07.15C12.96 17.55 11 21 11 21z";

    /// <summary>账号。</summary>
    public const string Account =
        "M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-3.31 0-6 2.24-6 5v1h12v-1c0-2.76-2.69-5-6-5z";

    /// <summary>表格。</summary>
    public const string TableRows =
        "M21 8H3V4h18v4zm0 2H3v4h18v-4zm0 6H3v4h18v-4z";

    /// <summary>时钟。</summary>
    public const string Clock =
        "M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10 10-4.5 10-10S17.5 2 12 2zm4.2 14.2L11 13V7h1.5v5.2l4.5 2.7-.8 1.3z";
}
