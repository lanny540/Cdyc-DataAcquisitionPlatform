using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示班组能耗报表页的交互逻辑。
///
/// 报表按「班组 × 指标 × 统计口径」汇总，数据全部来自既有能耗历史接口，
/// 不额外引入后端契约；每个班组各发一次历史查询，并发执行后在前端拼装。
/// </summary>
public partial class EnergyReport
{
    private const string GranularityDay = "day";
    private const string GranularityWeek = "week";
    private const string GranularityMonth = "month";

    /// <summary>
    /// 四张统计卡依次取用的语义色，与其余能耗页面保持一致。
    /// </summary>
    private static readonly string[] MetricAccents =
        ["platform-accent-info", "platform-accent-success", "platform-accent-warning", "platform-accent-tertiary"];

    private static readonly string[] SummaryIcons =
    [
        Icons.Material.Filled.Groups,
        Icons.Material.Filled.EventRepeat,
        Icons.Material.Filled.Science,
        Icons.Material.Filled.TableRows
    ];

    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;

    private DateTime? _fromDate = DateTime.Today.AddDays(-29);
    private DateTime? _toDate = DateTime.Today;
    private string _granularity = GranularityDay;

    private bool _isLoading = true;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;
    private string? _rangeLabel;

    private EnergyMetricDescriptorDto[] _metrics = [];
    private TeamReportRow[] _teamRows = [];
    private ReportDetailRow[] _detailRows = [];
    private decimal[] _metricTotals = [];

    /// <summary>
    /// 报表概览卡。数值在代码后置里格式化成字符串：Razor 里的嵌套插值不会求值。
    /// </summary>
    private SummaryCard[] SummaryCards =>
    [
        new("参与班组", _teamRows.Length.ToString(), "按当前统计口径汇总"),
        new("统计周期", MaxPeriods.ToString(), GranularityLabel(_granularity)),
        new("指标项", _metrics.Length.ToString(), _metrics.Length == 0 ? "未取到指标定义" : "四项原辅材料与能源"),
        new("明细记录", _detailRows.Length.ToString(), "逐条指标记录")
    ];

    private int MaxPeriods => _teamRows.Length == 0 ? 0 : _teamRows.Max(row => row.Periods);

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _isLoading = true;
        _message = null;

        try
        {
            (DateOnly from, DateOnly to) = ResolveRange();

            ShiftScheduleStatusDto status = await PlatformApiClient.GetShiftScheduleStatusAsync();
            ShiftTeamDto[] teams = status.Teams.ToArray();

            if (teams.Length == 0)
            {
                ResetResult();
                _message = "尚未导入班组排班数据，无法生成报表。请先在「业务配置 → 排班时间管理」中导入排班表。";
                _messageSeverity = Severity.Info;
                return;
            }

            EnergyHistoryDto[] histories = await Task.WhenAll(
                teams.Select(team => PlatformApiClient.GetEnergyHistoryAsync(team.TeamCode, _granularity, from, to)));

            BuildRows(histories);
            _rangeLabel = $"{from:yyyy-MM-dd} ~ {to:yyyy-MM-dd}";
        }
        catch (Exception ex)
        {
            ResetResult();
            _message = $"报表生成失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// 把各班组的查询结果拼成汇总行与明细行。
    /// </summary>
    private void BuildRows(EnergyHistoryDto[] histories)
    {
        _metrics = histories
            .SelectMany(history => history.Metrics)
            .GroupBy(metric => metric.MetricCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        _teamRows = histories
            .Select(history => new TeamReportRow(
                history.TeamCode,
                history.TeamName,
                _metrics
                    .Select(metric => history.Trends
                        .FirstOrDefault(trend => string.Equals(trend.MetricCode, metric.MetricCode,
                            StringComparison.OrdinalIgnoreCase))
                        ?.Total)
                    .ToArray(),
                history.Trends.Count == 0 ? 0 : history.Trends.Max(trend => trend.Points.Count)))
            .ToArray();

        _detailRows = histories
            .SelectMany(history => history.DetailRows.Select(detail => new ReportDetailRow(
                history.TeamName,
                detail.BucketStart,
                FormatBucket(detail.BucketStart, detail.BucketEnd),
                detail.MetricName,
                detail.Unit,
                detail.Value)))
            .OrderByDescending(row => row.BucketStart)
            .ThenBy(row => row.TeamName, StringComparer.Ordinal)
            .ThenBy(row => row.MetricName, StringComparer.Ordinal)
            .ToArray();

        _metricTotals = _metrics
            .Select((_, index) => _teamRows.Sum(row => row.Values[index] ?? 0m))
            .ToArray();
    }

    private void ResetResult()
    {
        _metrics = [];
        _teamRows = [];
        _detailRows = [];
        _metricTotals = [];
        _rangeLabel = null;
    }

    /// <summary>
    /// 起止日期若被选反，按升序纠正，避免服务端返回空区间。
    /// </summary>
    private (DateOnly From, DateOnly To) ResolveRange()
    {
        DateOnly from = DateOnly.FromDateTime(_fromDate ?? DateTime.Today);
        DateOnly to = DateOnly.FromDateTime(_toDate ?? DateTime.Today);

        return to < from ? (to, from) : (from, to);
    }

    private void OnFromDateChanged(DateTime? value)
    {
        _fromDate = value;
    }

    private void OnToDateChanged(DateTime? value)
    {
        _toDate = value;
    }

    private async Task OnGranularityChangedAsync(string value)
    {
        _granularity = value;
        await LoadAsync();
    }

    private static string GranularityLabel(string granularity)
    {
        return granularity switch
        {
            GranularityWeek => "统计周",
            GranularityMonth => "统计月",
            _ => "自然日"
        };
    }

    /// <summary>
    /// 周期文案：单日只显示日期，跨日区间显示起止。
    /// DateOnly 的格式化必须放在代码后置，Razor 的 @value:yyyy-MM-dd 对其不生效。
    /// </summary>
    private static string FormatBucket(DateOnly start, DateOnly end)
    {
        return start == end
            ? start.ToString("yyyy-MM-dd")
            : $"{start:yyyy-MM-dd} ~ {end:yyyy-MM-dd}";
    }

    private static string FormatValue(decimal value)
    {
        return value >= 1000m
            ? value.ToString("N0")
            : value.ToString("0.##");
    }

    private static string FormatNullable(decimal? value)
    {
        return value.HasValue ? FormatValue(value.Value) : "--";
    }

    private string BuildSummarySubtitle()
    {
        return $"{_teamRows.Length} 个班组 × {_metrics.Length} 项指标，口径为{GranularityLabel(_granularity)}。";
    }

    private sealed record SummaryCard(string Title, string Value, string Supporting);

    private sealed record TeamReportRow(string TeamCode, string TeamName, decimal?[] Values, int Periods);

    private sealed record ReportDetailRow(
        string TeamName,
        DateOnly BucketStart,
        string PeriodLabel,
        string MetricName,
        string Unit,
        decimal Value);
}
