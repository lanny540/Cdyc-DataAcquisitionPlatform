using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.AvaloniaApp.Common;
using DAP.Presentation.AvaloniaApp.Navigation;
using DAP.Presentation.AvaloniaApp.Services;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Pages;

/// <summary>
/// 班组能耗报表：按区间汇总各班组的四项指标，并给出逐条明细与 CSV 导出。
///
/// 与 Web 端同口径：报表不新增后端契约，按班组并发调用历史指标接口后在前端拼装。
/// </summary>
public sealed partial class EnergyReportPageViewModel : AutoRefreshPageViewModelBase
{
    private static readonly string[] MetricAccentKeys =
        ["DapInfoBrush", "DapSuccessBrush", "DapWarningBrush", "DapTertiaryBrush"];

    private static readonly string[] MetricStrongKeys =
        ["DapInfoStrongBrush", "DapSuccessStrongBrush", "DapWarningStrongBrush", "DapTertiaryStrongBrush"];

    private static readonly string[] MetricSoftKeys =
        ["DapInfoSoftBrush", "DapSuccessSoftBrush", "DapWarningSoftBrush", "DapTertiarySoftBrush"];

    private readonly DesktopSession _session;

    private EnergyMetricDescriptorDto[] _metrics = [];

    /// <summary>
    /// 初始化报表页。
    /// </summary>
    /// <param name="session">桌面会话。</param>
    public EnergyReportPageViewModel(DesktopSession session)
    {
        _session = session;

        GranularityOptions =
        [
            new GranularityOption("day", "按日"),
            new GranularityOption("week", "按周"),
            new GranularityOption("month", "按月")
        ];

        SelectedGranularity = GranularityOptions[0];
        FromDate = DateTime.Today.AddDays(-29);
        ToDate = DateTime.Today;
    }

    /// <inheritdoc />
    public override string PageKey => PageKeys.EnergyReport;

    /// <inheritdoc />
    public override string Title => "班组能耗报表";

    /// <inheritdoc />
    public override string Description => "按日期区间汇总各班组消耗，支持口径切换与 CSV 导出。";

    /// <inheritdoc />
    public override string Accent => "primary";

    /// <summary>
    /// 统计粒度选项。
    /// </summary>
    public ObservableCollection<GranularityOption> GranularityOptions { get; }

    /// <summary>
    /// 统计粒度。
    /// </summary>
    [ObservableProperty]
    private GranularityOption? _selectedGranularity;

    /// <summary>
    /// 起始日期。
    /// </summary>
    [ObservableProperty]
    private DateTime? _fromDate;

    /// <summary>
    /// 结束日期。
    /// </summary>
    [ObservableProperty]
    private DateTime? _toDate;

    /// <summary>
    /// 是否有报表数据。
    /// </summary>
    [ObservableProperty]
    private bool _hasData;

    /// <summary>
    /// 区间与口径文案。
    /// </summary>
    [ObservableProperty]
    private string _rangeText = string.Empty;

    /// <summary>
    /// 是否有可导出的明细。
    /// </summary>
    [ObservableProperty]
    private bool _canExport;

    /// <summary>
    /// 概览卡。
    /// </summary>
    public ObservableCollection<MetricCardModel> SummaryCards { get; } = [];

    /// <summary>
    /// 班组汇总行。
    /// </summary>
    public ObservableCollection<TeamReportRowModel> TeamRows { get; } = [];

    /// <summary>
    /// 明细行。
    /// </summary>
    public ObservableCollection<ReportDetailRowModel> DetailRows { get; } = [];

    /// <summary>
    /// 合计行的四个指标值。
    /// </summary>
    [ObservableProperty]
    private string _totalV1 = "--";

    /// <summary>
    /// 合计行第二列。
    /// </summary>
    [ObservableProperty]
    private string _totalV2 = "--";

    /// <summary>
    /// 合计行第三列。
    /// </summary>
    [ObservableProperty]
    private string _totalV3 = "--";

    /// <summary>
    /// 合计行第四列。
    /// </summary>
    [ObservableProperty]
    private string _totalV4 = "--";

    /// <summary>
    /// 第一列指标名。
    /// </summary>
    [ObservableProperty]
    private string _column1 = "指标 1";

    /// <summary>
    /// 第二列指标名。
    /// </summary>
    [ObservableProperty]
    private string _column2 = "指标 2";

    /// <summary>
    /// 第三列指标名。
    /// </summary>
    [ObservableProperty]
    private string _column3 = "指标 3";

    /// <summary>
    /// 第四列指标名。
    /// </summary>
    [ObservableProperty]
    private string _column4 = "指标 4";

    /// <summary>
    /// 明细条数文案。
    /// </summary>
    [ObservableProperty]
    private string _detailCountText = string.Empty;

    /// <inheritdoc />
    protected override async Task LoadCoreAsync()
    {
        ShiftScheduleStatusDto status = await _session.Api.GetShiftScheduleStatusAsync();
        ShiftTeamDto[] teams = status.Teams.ToArray();

        if (teams.Length == 0)
        {
            Reset();
            SetMessage("尚未导入班组排班数据，无法生成报表。请先在 Web 端「业务配置 → 排班时间管理」中导入排班表。");
            return;
        }

        GranularityOption granularity = SelectedGranularity ?? GranularityOptions[0];
        (DateOnly from, DateOnly to) = ResolveRange();

        EnergyHistoryDto[] histories = await Task.WhenAll(
            teams.Select(team => _session.Api.GetEnergyHistoryAsync(team.TeamCode, granularity.Code, from, to)));

        Build(histories, granularity, from, to);
    }

    /// <summary>
    /// 起止日期若被选反，按升序纠正，避免服务端返回空区间。
    /// </summary>
    private (DateOnly From, DateOnly To) ResolveRange()
    {
        DateOnly from = DateOnly.FromDateTime(FromDate ?? DateTime.Today);
        DateOnly to = DateOnly.FromDateTime(ToDate ?? DateTime.Today);

        return to < from ? (to, from) : (from, to);
    }

    /// <summary>
    /// 拼装汇总与明细。
    /// </summary>
    private void Build(EnergyHistoryDto[] histories, GranularityOption granularity, DateOnly from, DateOnly to)
    {
        _metrics = histories
            .SelectMany(history => history.Metrics)
            .GroupBy(metric => metric.MetricCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var metricTotals = new decimal[_metrics.Length];

        TeamRows.Clear();
        DetailRows.Clear();
        SummaryCards.Clear();

        foreach (EnergyHistoryDto history in histories)
        {
            decimal?[] values = _metrics
                .Select(metric => history.Trends
                    .FirstOrDefault(trend => string.Equals(trend.MetricCode, metric.MetricCode,
                        StringComparison.OrdinalIgnoreCase))
                    ?.Total)
                .ToArray();

            for (var index = 0; index < values.Length; index++)
            {
                metricTotals[index] += values[index] ?? 0m;
            }

            var periods = history.Trends.Count == 0 ? 0 : history.Trends.Max(trend => trend.Points.Count);

            TeamRows.Add(new TeamReportRowModel(
                history.TeamName,
                MetricFormat.Value(values.ElementAtOrDefault(0)),
                MetricFormat.Value(values.ElementAtOrDefault(1)),
                MetricFormat.Value(values.ElementAtOrDefault(2)),
                MetricFormat.Value(values.ElementAtOrDefault(3)),
                $"{periods} 个周期"));

            foreach (EnergyMetricValueDto detail in history.DetailRows
                         .OrderByDescending(row => row.BucketStart)
                         .ThenBy(row => row.MetricName, StringComparer.Ordinal))
            {
                DetailRows.Add(new ReportDetailRowModel(
                    history.TeamName,
                    MetricFormat.Bucket(detail.BucketStart, detail.BucketEnd),
                    detail.MetricName,
                    MetricFormat.Value(detail.Value),
                    detail.Unit));
            }
        }

        HasData = TeamRows.Count > 0;
        CanExport = HasData;
        RangeText = $"{MetricFormat.Range(from, to)} · {granularity.Label}";
        DetailCountText = $"{DetailRows.Count} 条明细，与上方汇总同源";

        Column1 = _metrics.ElementAtOrDefault(0)?.MetricName ?? "指标 1";
        Column2 = _metrics.ElementAtOrDefault(1)?.MetricName ?? "指标 2";
        Column3 = _metrics.ElementAtOrDefault(2)?.MetricName ?? "指标 3";
        Column4 = _metrics.ElementAtOrDefault(3)?.MetricName ?? "指标 4";

        TotalV1 = MetricFormat.Value(metricTotals.ElementAtOrDefault(0));
        TotalV2 = MetricFormat.Value(metricTotals.ElementAtOrDefault(1));
        TotalV3 = MetricFormat.Value(metricTotals.ElementAtOrDefault(2));
        TotalV4 = MetricFormat.Value(metricTotals.ElementAtOrDefault(3));

        SummaryCards.Add(new MetricCardModel("参与班组", TeamRows.Count.ToString(), "按当前统计口径汇总",
            NavIcons.Account, Palette.Brush(MetricAccentKeys[0]), Palette.Brush(MetricStrongKeys[0]),
            Palette.Brush(MetricSoftKeys[0]), Palette.Brush("DapSurfaceBrush")));
        SummaryCards.Add(new MetricCardModel("统计周期",
            TeamRows.Count == 0 ? "0" : TeamRows.Max(row => ParsePeriods(row.Periods)).ToString(),
            granularity.Label, NavIcons.Clock, Palette.Brush(MetricAccentKeys[1]),
            Palette.Brush(MetricStrongKeys[1]), Palette.Brush(MetricSoftKeys[1]),
            Palette.Brush("DapSurfaceBrush")));
        SummaryCards.Add(new MetricCardModel("指标项", _metrics.Length.ToString(),
            _metrics.Length == 0 ? "未取到指标定义" : "四项原辅材料与能源",
            NavIcons.Science, Palette.Brush(MetricAccentKeys[2]), Palette.Brush(MetricStrongKeys[2]),
            Palette.Brush(MetricSoftKeys[2]), Palette.Brush("DapSurfaceBrush")));
        SummaryCards.Add(new MetricCardModel("明细记录", DetailRows.Count.ToString(), "逐条指标记录",
            NavIcons.TableRows, Palette.Brush(MetricAccentKeys[3]), Palette.Brush(MetricStrongKeys[3]),
            Palette.Brush(MetricSoftKeys[3]), Palette.Brush("DapSurfaceBrush")));
    }

    /// <summary>
    /// 从「N 个周期」中取出数字，用于统计周期卡。
    /// </summary>
    private static int ParsePeriods(string text)
    {
        return int.TryParse(text.Split(' ')[0], NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            ? value
            : 0;
    }

    /// <summary>
    /// 生成 CSV 文本。带 UTF-8 BOM 写入，Excel 打开中文不乱码。
    /// </summary>
    /// <returns>CSV 文本。</returns>
    public string BuildCsv()
    {
        var builder = new StringBuilder();

        builder.AppendLine($"班组能耗报表,{RangeText}");
        builder.AppendLine("班组,统计周期,指标,数值,单位");

        foreach (ReportDetailRowModel row in DetailRows)
        {
            builder.AppendLine(string.Join(',',
                Escape(row.TeamName),
                Escape(row.Period),
                Escape(row.MetricName),
                Escape(row.Value),
                Escape(row.Unit)));
        }

        builder.AppendLine();
        builder.AppendLine("班组汇总");
        builder.AppendLine($"班组,{Column1},{Column2},{Column3},{Column4},周期数");

        foreach (TeamReportRowModel row in TeamRows)
        {
            builder.AppendLine(string.Join(',',
                Escape(row.TeamName),
                Escape(row.V1),
                Escape(row.V2),
                Escape(row.V3),
                Escape(row.V4),
                Escape(row.Periods)));
        }

        builder.AppendLine(string.Join(',', "合计", Escape(TotalV1), Escape(TotalV2), Escape(TotalV3),
            Escape(TotalV4), "--"));

        return builder.ToString();
    }

    /// <summary>
    /// CSV 转义：含逗号、引号或换行的字段用双引号包裹，内部引号翻倍。
    /// </summary>
    private static string Escape(string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    /// <summary>
    /// 清空结果。
    /// </summary>
    private void Reset()
    {
        _metrics = [];
        HasData = false;
        CanExport = false;
        TeamRows.Clear();
        DetailRows.Clear();
        SummaryCards.Clear();
        RangeText = string.Empty;
        DetailCountText = string.Empty;
        TotalV1 = TotalV2 = TotalV3 = TotalV4 = "--";
    }
}
