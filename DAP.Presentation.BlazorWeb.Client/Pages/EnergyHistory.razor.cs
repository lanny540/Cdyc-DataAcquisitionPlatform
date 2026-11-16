using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示班组历史指标页的交互逻辑。
/// </summary>
public partial class EnergyHistory
{
    /// <summary>
    /// 折线图统一呈现配置：数据点标记、加粗线条、坐标轴线与悬停提示。
    /// 每张卡的指标单位不同，纵轴标题按指标动态生成。
    /// </summary>
    private static LineChartOptions BuildOptions(EnergyTrendDto trend)
    {
        return PlatformChartDefaults.CreateLineOptions(trend.Unit, "统计周期");
    }

    private const string GranularityDay = "day";
    private const string GranularityWeek = "week";
    private const string GranularityMonth = "month";

    /// <summary>
    /// 四项指标依次取用的语义色，与仪表板页保持一致。
    /// </summary>
    private static readonly string[] MetricAccents =
        ["platform-accent-info", "platform-accent-success", "platform-accent-warning", "platform-accent-tertiary"];

    private static readonly string[] MetricIcons =
    [
        Icons.Material.Filled.Science,
        Icons.Material.Filled.Opacity,
        Icons.Material.Filled.Grain,
        Icons.Material.Filled.ElectricalServices
    ];

    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;

    private EnergyHistoryDto? _history;
    private IReadOnlyCollection<ShiftTeamDto> _teams = [];

    /// <summary>
    /// DTO 集合声明为 IReadOnlyCollection，页面按下标访问前先转成数组。
    /// </summary>
    private EnergyTrendDto[] Trends => _history?.Trends.ToArray() ?? [];

    private ShiftTeamDto[] Teams => _teams.ToArray();

    private string _teamCode = "TEAM-甲";
    private string _granularity = GranularityDay;
    private bool _isLoading = true;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;

    /// <summary>
    /// 自动刷新开关。默认关闭，需要看板常驻时由用户打开。
    /// </summary>
    private bool _autoRefreshEnabled;

    /// <summary>
    /// 自动刷新间隔（秒）。
    /// </summary>
    private int _refreshIntervalSeconds = 60;

    /// <summary>
    /// 最近一次成功加载时间，用于「数据截至 HH:mm:ss」。
    /// </summary>
    private DateTimeOffset? _lastUpdatedAt;

    /// <summary>
    /// 是否正在静默刷新：与首载遮罩分开，静默刷新不清数据、不换整页加载态。
    /// </summary>
    private bool _isRefreshing;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    /// <summary>
    /// 首载与手动刷新：显示整页遮罩，失败时清空数据。
    /// </summary>
    private Task LoadAsync()
    {
        return LoadInternalAsync(silent: false);
    }

    /// <summary>
    /// 定时刷新：不换遮罩、保留旧数据，失败只出一条警告提示。
    /// </summary>
    private Task RefreshSilentlyAsync()
    {
        return LoadInternalAsync(silent: true);
    }

    /// <param name="silent">true 表示定时触发的静默刷新。</param>
    private async Task LoadInternalAsync(bool silent)
    {
        if (silent && (_isLoading || _isRefreshing))
        {
            return;
        }

        if (silent)
        {
            _isRefreshing = true;
        }
        else
        {
            _isLoading = true;
            _message = null;
        }

        try
        {
            ShiftScheduleStatusDto status = await PlatformApiClient.GetShiftScheduleStatusAsync();
            _teams = status.Teams;

            // 已导入的班组可能不含甲班，此时回退到第一个真实班组。
            if (Teams.Length > 0 && Teams.All(team => team.TeamCode != _teamCode))
            {
                _teamCode = Teams[0].TeamCode;
            }

            DateOnly today = DateOnly.FromDateTime(DateTime.Today);
            (DateOnly from, DateOnly to) = ResolveRange(today);

            _history = await PlatformApiClient.GetEnergyHistoryAsync(_teamCode, _granularity, from, to);

            _lastUpdatedAt = DateTimeOffset.Now;
            _message = null;
        }
        catch (Exception ex)
        {
            _message = silent
                ? $"自动刷新未成功，页面仍显示上次加载的数据：{UserFacingErrors.Describe(ex)}"
                : $"历史指标加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = silent ? Severity.Warning : Severity.Error;

            if (!silent)
            {
                _history = null;
            }
        }
        finally
        {
            if (silent)
            {
                _isRefreshing = false;
            }
            else
            {
                _isLoading = false;
            }
        }
    }

    /// <summary>
    /// 按统计粒度推导查询区间。月粒度取近 12 个月，避免一次拉取过多数据点。
    /// </summary>
    private (DateOnly From, DateOnly To) ResolveRange(DateOnly today)
    {
        return _granularity switch
        {
            GranularityWeek => (today.AddDays(-83), today),
            GranularityMonth => (today.AddMonths(-12), today),
            _ => (today.AddDays(-29), today)
        };
    }

    private async Task OnTeamChangedAsync(string value)
    {
        _teamCode = value;
        await LoadAsync();
    }

    private async Task OnGranularityChangedAsync(string value)
    {
        _granularity = value;
        await LoadAsync();
    }

    private static List<ChartSeries<double>> BuildSeries(EnergyTrendDto trend)
    {
        return
        [
            new ChartSeries<double>
            {
                Name = trend.MetricName,
                Data = trend.Points.Select(item => (double) item.Value).ToArray()
            }
        ];
    }

    /// <summary>
    /// X 轴标签抽稀：数据点较多时只保留若干个标签，避免文字挤成一条黑带。
    /// MudChart 会等距绘制所有标签，全量输出在 60 个点以上完全无法阅读。
    /// </summary>
    private static string[] BuildLabels(EnergyTrendDto trend)
    {
        IReadOnlyCollection<EnergyTrendPointDto> points = trend.Points;
        if (points.Count == 0)
        {
            return [];
        }

        const int maxLabels = 12;

        if (points.Count <= maxLabels)
        {
            return points.Select(item => item.Label).ToArray();
        }

        var step = (int) Math.Ceiling(points.Count / (double) maxLabels);

        return points
            .Select((point, index) => index % step == 0 ? point.Label : string.Empty)
            .ToArray();
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
    /// 区间文案在代码后置格式化：Razor 的 `@value:yyyy-MM-dd` 对 DateOnly 不生效，会输出格式串本身。
    /// </summary>
    private static string FormatRange(DateOnly from, DateOnly to)
    {
        return $"{from:yyyy-MM-dd} ~ {to:yyyy-MM-dd}";
    }

    private static string TrendLabel(string direction)
    {
        return direction switch
        {
            "up" => "后半程上升",
            "down" => "后半程下降",
            _ => "走势平稳"
        };
    }

    /// <summary>
    /// 消耗上升用警示色、下降用成功色，平稳不加语义色。
    /// </summary>
    private static Color GetTrendColor(string direction)
    {
        return direction switch
        {
            "up" => Color.Error,
            "down" => Color.Success,
            _ => Color.Default
        };
    }

    private static string FormatValue(decimal value)
    {
        return value >= 1000m
            ? value.ToString("N0")
            : value.ToString("0.##");
    }

    /// <summary>
    /// 图例文案在代码后置里拼接：Razor 的 `@($"...@Method()...")` 嵌套求值会失效并原样输出表达式。
    /// </summary>
    private static string BuildTrendLegend(EnergyTrendDto trend)
    {
        return $"共 {trend.Points.Count} 个数据点，区间合计 {FormatValue(trend.Total)} {trend.Unit}";
    }

    /// <summary>
    /// 图表卡片副标题。纯文本中出现的 `@Method()` 不会被 Razor 求值，
    /// 必须整体放进 `@(...)` 或直接用属性返回拼好的文案。
    /// </summary>
    private string BuildTrendSubtitle(EnergyTrendDto trend)
    {
        return $"{_history?.TeamName ?? "--"}，单位 {trend.Unit}";
    }

    private static string BuildAverageSummary(EnergyTrendDto trend)
    {
        return $"区间均值 {trend.Unit}，峰值 {FormatValue(trend.Peak)}";
    }

    /// <summary>
    /// 把长表按时间点转成宽表，一行一个统计周期，四列对应四项指标。
    /// </summary>
    private IReadOnlyList<PivotRow> _pivotRows =>
        _history is null
            ? []
            : Trends
                .SelectMany(trend => trend.Points.Select(point =>
                    new {point.BucketStart, point.Label, trend.MetricCode, point.Value}))
                .GroupBy(item => item.BucketStart)
                .OrderBy(group => group.Key)
                .Select(group => new PivotRow(
                    group.First().Label,
                    _history.Metrics
                        .Select(metric => group.FirstOrDefault(item => item.MetricCode == metric.MetricCode)?.Value)
                        .ToArray()))
                .ToArray();

    private sealed record PivotRow(string Label, decimal?[] Values);
}
