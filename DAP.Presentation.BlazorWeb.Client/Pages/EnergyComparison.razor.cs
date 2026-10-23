using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示班组横向对比页的交互逻辑。
/// </summary>
public partial class EnergyComparison
{
    /// <summary>
    /// 柱状图统一呈现配置：坐标轴线、刻度数量与悬停提示。
    /// 每张卡的指标单位不同，纵轴标题按指标动态生成。
    /// </summary>
    private static BarChartOptions BuildOptions(EnergyMetricDescriptorDto metric)
    {
        return PlatformChartDefaults.CreateBarOptions(metric.Unit, "班组");
    }

    private const string GranularityDay = "day";
    private const string GranularityWeek = "week";
    private const string GranularityMonth = "month";

    /// <summary>
    /// 四项指标依次取用的语义色，与其余能耗页面保持一致。
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

    private EnergyComparisonDto? _comparison;
    private string _granularity = GranularityDay;

    /// <summary>
    /// MudDatePicker 绑定的是 DateTime?，服务端契约用 DateOnly，这里做一次转换。
    /// </summary>
    private DateTime? _referenceDate = DateTime.Today;

    private bool _isLoading = true;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;

    private decimal _highestTotal;
    private decimal _lowestTotal;
    private decimal _spread;

    /// <summary>
    /// DTO 集合声明为 IReadOnlyCollection，页面按下标或遍历前先转成数组。
    /// </summary>
    private EnergyMetricDescriptorDto[] Metrics => _comparison?.Metrics.ToArray() ?? [];

    private TeamEnergyComparisonDto[] Teams => _comparison?.Teams.ToArray() ?? [];

    private string HighestTeamName => _comparison is null
        ? "--"
        : Teams.MaxBy(item => item.TotalConsumption)?.TeamName ?? "--";

    private string LowestTeamName => _comparison is null
        ? "--"
        : Teams.MinBy(item => item.TotalConsumption)?.TeamName ?? "--";

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
            DateOnly reference = _referenceDate.HasValue
                ? DateOnly.FromDateTime(_referenceDate.Value)
                : DateOnly.FromDateTime(DateTime.Today);

            _comparison = await PlatformApiClient.GetEnergyComparisonAsync(_granularity, reference);

            if (_comparison.Teams.Count > 0)
            {
                _highestTotal = _comparison.Teams.Max(item => item.TotalConsumption);
                _lowestTotal = _comparison.Teams.Min(item => item.TotalConsumption);
                _spread = _highestTotal - _lowestTotal;
            }
        }
        catch (Exception ex)
        {
            _message = $"班组对比加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
            _comparison = null;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task OnGranularityChangedAsync(string value)
    {
        _granularity = value;
        await LoadAsync();
    }

    private async Task OnReferenceDateChangedAsync(DateTime? value)
    {
        _referenceDate = value;
        await LoadAsync();
    }

    /// <summary>
    /// 构建单项指标的班组对比柱状图。
    ///
    /// MudChart 的数据结构是「系列 × X 轴标签」：
    /// 每个系列是一组柱子，X 轴标签决定柱子的横向位置。
    /// 因此要实现「横轴为班组、每班组一根柱子」，必须
    /// **只建一条系列（系列名 = 指标名），把各班组数值作为该系列的数据点**，
    /// X 轴标签传班组名。此前误把每个班组建成一条系列，导致 X 轴只剩一个标签、
    /// 柱子在同一位置重叠成一束，横向对比反而看不清。
    /// </summary>
    private List<ChartSeries<double>> BuildTeamSeries(string metricCode)
    {
        if (_comparison is null)
        {
            return [];
        }

        var metricName = Metrics
            .FirstOrDefault(metric => metric.MetricCode == metricCode)?.MetricName ?? metricCode;

        return
        [
            new ChartSeries<double>
            {
                Name = metricName,
                Data = Teams
                    .Select(team => (double) (team.MetricValues
                        .FirstOrDefault(item => item.MetricCode == metricCode)
                        ?.Value ?? 0m))
                    .ToArray()
            }
        ];
    }

    private string[] BuildTeamLabels()
    {
        return Teams.Select(item => item.TeamName).ToArray();
    }

    private static string GranularityLabel(string granularity)
    {
        return granularity switch
        {
            GranularityWeek => "周",
            GranularityMonth => "月",
            _ => "日"
        };
    }

    private static string FormatValue(decimal value)
    {
        return value >= 1000m
            ? value.ToString("N0")
            : value.ToString("0.##");
    }

    /// <summary>
    /// 区间文案在代码后置格式化：Razor 的 `@value:yyyy-MM-dd` 对 DateOnly 不生效，会输出格式串本身。
    /// </summary>
    private static string FormatRange(DateOnly from, DateOnly to)
    {
        return $"{from:yyyy-MM-dd} ~ {to:yyyy-MM-dd}";
    }

    /// <summary>
    /// 图例文案在代码后置里拼接：Razor 的 `@($"...@Method()...")` 嵌套求值会失效并原样输出表达式。
    /// </summary>
    /// <summary>
    /// 图例文案：柱子代表班组，系列代表当前指标。
    /// </summary>
    private string BuildTeamLegend(EnergyMetricDescriptorDto metric)
    {
        return $"{Teams.Length} 个班组在 {metric.MetricName} 上的对比（单位 {metric.Unit}）";
    }

    /// <summary>
    /// 图表卡片副标题。纯文本中出现的 `@Method()` 不会被 Razor 求值，
    /// 必须整体放进 `@(...)` 或直接用属性返回拼好的文案。
    /// </summary>
    private string BuildMetricSubtitle(EnergyMetricDescriptorDto metric)
    {
        return $"单位 {metric.Unit}，按{GranularityLabel(_comparison?.Granularity ?? "day")}对比";
    }
}
