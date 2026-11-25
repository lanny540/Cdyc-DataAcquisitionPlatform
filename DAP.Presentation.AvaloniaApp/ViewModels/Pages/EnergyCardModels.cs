using Avalonia.Media;
using DAP.Presentation.AvaloniaApp.Controls;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Pages;

/// <summary>
/// 指标统计卡。
///
/// 2026-11-25 对齐 MD2 示意稿卡片语言：白卡 + MD2 投影，不铺 tint 底纹、不加色条；
/// 色相只出现在圆形图标底块（accent 实底 + On 档图标）。
/// </summary>
/// <param name="Label">指标名称。</param>
/// <param name="Value">数值文本。</param>
/// <param name="Supporting">补充说明。</param>
/// <param name="Icon">图标路径数据。</param>
/// <param name="Accent">主色 base 档，用于圆形图标底块底色。</param>
/// <param name="AccentStrong">主色 strong 加深档，用于图表系列。</param>
/// <param name="AccentSoft">主色 soft 档（保留兼容，当前未用于图标底块）。</param>
/// <param name="Background">卡片底色（中性表面色，白卡）。</param>
/// <param name="AccentOn">图标底块上的前景（多数为白，琥珀底为深褐）。</param>
public sealed record MetricCardModel(
    string Label,
    string Value,
    string Supporting,
    string Icon,
    IBrush Accent,
    IBrush AccentStrong,
    IBrush AccentSoft,
    IBrush Background,
    IBrush AccentOn);

/// <summary>
/// 指标趋势卡：一张卡一项指标，避免四项指标挤在同一张图上互相压线。
/// </summary>
/// <param name="MetricName">指标名称。</param>
/// <param name="Unit">单位。</param>
/// <param name="Subtitle">副标题。</param>
/// <param name="TotalText">区间合计文本。</param>
/// <param name="AverageText">区间均值文本。</param>
/// <param name="DirectionLabel">趋势方向文案。</param>
/// <param name="DirectionAccent">趋势方向颜色（strong 亮色档）。</param>
/// <param name="Samples">图表数据。</param>
/// <param name="Accent">主色 base 档，用于左侧色条。</param>
/// <param name="AccentStrong">主色 strong 亮色档，用于图表折线。</param>
/// <param name="Background">卡片底色（中性表面色）。</param>
public sealed record TrendCardModel(
    string MetricName,
    string Unit,
    string Subtitle,
    string TotalText,
    string AverageText,
    string DirectionLabel,
    IBrush DirectionAccent,
    IReadOnlyList<ChartSample> Samples,
    IBrush Accent,
    IBrush AccentStrong,
    IBrush Background);

/// <summary>
/// 透视表的一行：一行一个统计周期，四列对应四项指标。
/// </summary>
/// <param name="Label">周期标签。</param>
/// <param name="V1">指标 1 数值。</param>
/// <param name="V2">指标 2 数值。</param>
/// <param name="V3">指标 3 数值。</param>
/// <param name="V4">指标 4 数值。</param>
public sealed record PivotRowModel(string Label, string V1, string V2, string V3, string V4);

/// <summary>
/// 报表汇总行：一个班组一行。
/// </summary>
/// <param name="TeamName">班组名称。</param>
/// <param name="V1">指标 1 合计。</param>
/// <param name="V2">指标 2 合计。</param>
/// <param name="V3">指标 3 合计。</param>
/// <param name="V4">指标 4 合计。</param>
/// <param name="Periods">统计周期数。</param>
public sealed record TeamReportRowModel(
    string TeamName,
    string V1,
    string V2,
    string V3,
    string V4,
    string Periods);

/// <summary>
/// 统计粒度选项。
/// </summary>
/// <param name="Code">服务端口径代码，取值 day / week / month。</param>
/// <param name="Label">中文名称。</param>
public sealed record GranularityOption(string Code, string Label)
{
    /// <inheritdoc />
    public override string ToString()
    {
        return Label;
    }
}

/// <summary>
/// 班组对比柱状图卡：一张卡一项指标，横轴为班组。
/// </summary>
/// <param name="MetricName">指标名称。</param>
/// <param name="Subtitle">副标题。</param>
/// <param name="Unit">单位。</param>
/// <param name="Legend">图例说明。</param>
/// <param name="Items">柱状数据。</param>
/// <param name="Accent">主色 base 档，用于左侧色条。</param>
/// <param name="AccentStrong">主色 strong 亮色档，用于柱形。</param>
/// <param name="Background">卡片底色（中性表面色）。</param>
public sealed record BarMetricCardModel(
    string MetricName,
    string Subtitle,
    string Unit,
    string Legend,
    IReadOnlyList<ChartSample> Items,
    IBrush Accent,
    IBrush AccentStrong,
    IBrush Background);

/// <summary>
/// 班组对比明细行。
/// </summary>
/// <param name="TeamName">班组名称。</param>
/// <param name="V1">指标 1。</param>
/// <param name="V2">指标 2。</param>
/// <param name="V3">指标 3。</param>
/// <param name="V4">指标 4。</param>
/// <param name="Total">综合能耗。</param>
/// <param name="ShiftCount">出勤班次。</param>
/// <param name="TotalHours">累计工时。</param>
/// <param name="Badge">最高 / 最低标记，无标记时为空。</param>
/// <param name="BadgeAccent">标记颜色。</param>
public sealed record TeamComparisonRowModel(
    string TeamName,
    string V1,
    string V2,
    string V3,
    string V4,
    string Total,
    string ShiftCount,
    string TotalHours,
    string Badge,
    IBrush BadgeAccent);

/// <summary>
/// 报表明细行。
/// </summary>
/// <param name="TeamName">班组名称。</param>
/// <param name="Period">统计周期文案。</param>
/// <param name="MetricName">指标名称。</param>
/// <param name="Value">数值文本。</param>
/// <param name="Unit">单位。</param>
public sealed record ReportDetailRowModel(
    string TeamName,
    string Period,
    string MetricName,
    string Value,
    string Unit);
