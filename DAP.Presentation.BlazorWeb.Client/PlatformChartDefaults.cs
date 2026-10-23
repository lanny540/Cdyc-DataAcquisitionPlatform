using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client;

/// <summary>
/// 统一的图表呈现配置。
///
/// **为什么需要集中配置**：三处图表页（仪表板、历史指标、横向对比）此前各自裸用 MudChart，
/// 出现两个问题——
///
/// 1. **图表没有充满卡片**。MudChart 的 SVG 默认使用固定 viewBox（如 `0 0 700 350`）并按比例居中，
///    容器宽高比与之不同时，SVG 内部会留出大片空白（实测左右各空 64px，绘图区仅占 79% 宽度）。
///    `MatchBoundsToSize = true` 让 SVG 按容器实际尺寸铺满。
///
/// 2. **信息不直观**。默认样式下数据点没有标记、线条偏细、坐标轴无标题与网格线，
///    用户需要盯着细线猜数值。下面的配置把数据点、轴线、轴标题、刻度数量都显式打开。
///
/// 放在共享位置而非各页重复，是为了让"图表长什么样"在全站只有一个答案。
/// </summary>
public static class PlatformChartDefaults
{
    /// <summary>
    /// 折线图选项：适合看趋势与波动。
    /// </summary>
    /// <param name="yAxisTitle">纵轴标题（通常是单位）。</param>
    /// <param name="xAxisTitle">横轴标题（通常是时间维度）。</param>
    public static LineChartOptions CreateLineOptions(string? yAxisTitle = null, string? xAxisTitle = null)
    {
        return new LineChartOptions
        {
            // 数据点标记 —— 没有它，折线上的数值只能靠位置估读
            ShowDataMarkers = true,

            // 线条加粗，避免细线在浅色底上难以辨认
            LineStrokeWidth = 3,

            // 纵轴从 0 起算，折线的相对高低才不失真
            YAxisRequireZeroPoint = false,

            // 保留网格线，便于对照读数
            XAxisLines = true,
            YAxisLines = true,

            // 刻度数量：过多会挤成黑带，过少则读不出量级
            YAxisTicks = 5,
            MaxNumYAxisTicks = 5,

            // 悬停提示 —— 直接给出数值，不必再配明细表
            ShowToolTips = true,

            // 轴标题：没有它，用户只能猜纵轴是什么量纲
            YAxisTitle = yAxisTitle,
            XAxisTitle = xAxisTitle
        };
    }

    /// <summary>
    /// 柱状图选项：适合跨班组横向比较。
    /// </summary>
    /// <param name="yAxisTitle">纵轴标题（通常是单位）。</param>
    /// <param name="xAxisTitle">横轴标题（通常是班组）。</param>
    public static BarChartOptions CreateBarOptions(string? yAxisTitle = null, string? xAxisTitle = null)
    {
        return new BarChartOptions
        {
            XAxisLines = true,
            YAxisLines = true,
            YAxisTicks = 5,
            MaxNumYAxisTicks = 5,
            ShowToolTips = true,

            // 注意：BarChartOptions 没有 YAxisRequireZeroPoint（仅折线图选项有），
            // 柱状图纵轴由 MudChart 按数据最小值自动缩放，无法强制从 0 起算。
            YAxisTitle = yAxisTitle,
            XAxisTitle = xAxisTitle
        };
    }
}
