using Avalonia.Media;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Pages;

/// <summary>
/// 实时采集记录行。
/// </summary>
/// <param name="PointCode">采集点编码。</param>
/// <param name="PointName">采集点名称。</param>
/// <param name="MetricName">指标名称。</param>
/// <param name="Value">数值。</param>
/// <param name="Unit">单位。</param>
/// <param name="CollectedAt">采集时间。</param>
public sealed record CollectionRecordRowModel(
    string PointCode,
    string PointName,
    string MetricName,
    string Value,
    string Unit,
    string CollectedAt);

/// <summary>
/// 排班日历单元格。
/// </summary>
/// <param name="DateText">日期文本（日）。</param>
/// <param name="InMonth">是否属于当前月份。</param>
/// <param name="OnDuty">在岗班组。</param>
/// <param name="Rest">轮休班组。</param>
/// <param name="Coverage">覆盖是否完整。</param>
/// <param name="Background">单元格底色。</param>
/// <param name="Accent">强调色。</param>
public sealed record CalendarCellModel(
    string DateText,
    string DateLabel,
    bool InMonth,
    string OnDuty,
    string Rest,
    bool Coverage,
    IBrush Background,
    IBrush Accent);
