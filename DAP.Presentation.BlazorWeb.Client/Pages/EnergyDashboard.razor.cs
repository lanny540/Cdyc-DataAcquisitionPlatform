using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示班组能耗仪表板页的交互逻辑。
/// </summary>
public partial class EnergyDashboard
{
    /// <summary>
    /// 折线图统一呈现配置：数据点标记、加粗线条、坐标轴线与悬停提示。
    /// 每张卡的指标单位不同，纵轴标题按指标动态生成。
    /// </summary>
    private static LineChartOptions BuildOptions(EnergyTrendDto trend)
    {
        return PlatformChartDefaults.CreateLineOptions(trend.Unit, "日期");
    }

    private static readonly int[] TrendDayOptions = [3, 7, 14, 30];

    /// <summary>
    /// 四项指标依次取用的语义色，必须来自设计规范既有的 accent 类，不得新增色相。
    /// </summary>
    private static readonly string[] MetricAccents =
        ["platform-accent-info", "platform-accent-success", "platform-accent-warning", "platform-accent-tertiary"];

    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;

    private EnergyDashboardDto? _dashboard;
    private IReadOnlyCollection<ShiftTeamDto> _teams = [];

    /// <summary>
    /// DTO 中的集合声明为 IReadOnlyCollection，页面按下标访问前先转成数组。
    /// </summary>
    private EnergyMetricValueDto[] MetricTotals => _dashboard?.MetricTotals.ToArray() ?? [];

    private EnergyTrendDto[] Trends => _dashboard?.Trends.ToArray() ?? [];

    private string? _teamCode;
    private int _trendDays = 7;
    private bool _isLoading = true;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;

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
            ShiftScheduleStatusDto status = await PlatformApiClient.GetShiftScheduleStatusAsync();
            _teams = status.Teams;

            _dashboard = await PlatformApiClient.GetEnergyDashboardAsync(_trendDays, _teamCode);

            // 首次加载后回填服务端推断出的当前班组，便于用户直接切换。
            _teamCode ??= _dashboard.TeamCode;
        }
        catch (Exception ex)
        {
            _message = $"能耗数据加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
            _dashboard = null;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task OnTeamChangedAsync(string value)
    {
        _teamCode = value;
        await LoadAsync();
    }

    private async Task OnTrendDaysChangedAsync(int value)
    {
        _trendDays = value;
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
    /// X 轴标签抽稀：MudChart 会等距绘制所有标签，7 天以上需限制数量才不至于挤成黑带。
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

    private static string GetMetricIcon(int index)
    {
        return index switch
        {
            0 => Icons.Material.Filled.Science,
            1 => Icons.Material.Filled.Opacity,
            2 => Icons.Material.Filled.Grain,
            3 => Icons.Material.Filled.ElectricalServices,
            _ => Icons.Material.Filled.ShowChart
        };
    }

    private static string TrendLabel(string direction)
    {
        return direction switch
        {
            "up" => "上升",
            "down" => "下降",
            _ => "平稳"
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

    /// <summary>
    /// 图例文案在代码后置里拼接。Razor 对 `@($"...@Method()...")` 这类嵌套求值并不可靠，
    /// 写成代码后会原样把表达式输出到页面上，因此统一走这里。
    /// </summary>
    private static string BuildPeakLegend(EnergyTrendDto trend)
    {
        return $"{trend.MetricName}（峰值 {FormatValue(trend.Peak)} {trend.Unit}）";
    }

    /// <summary>
    /// 指标卡片的补充说明。日期格式必须放在代码后置里格式化，
    /// Razor 的 `@value:format` 对 DateOnly 成员访问不生效，会把格式串原样输出。
    /// </summary>
    private static string BuildMetricSupporting(EnergyMetricValueDto value)
    {
        return $"单位 {value.Unit}，{value.BucketStart:yyyy-MM-dd} 当班累计";
    }

    /// <summary>
    /// 电力数值量级远高于原辅材料，保留一位小数避免出现长串数字挤占卡片。
    /// </summary>
    private static string FormatValue(decimal value)
    {
        return value >= 1000m
            ? value.ToString("N0")
            : value.ToString("0.##");
    }

    private static string FormatTotal(decimal value)
    {
        return value >= 1000m
            ? value.ToString("N1")
            : value.ToString("0.##");
    }

    /// <summary>
    /// 明细表按自然日合并四项指标，与上方图表的横轴保持一致。
    /// </summary>
    private IReadOnlyList<PivotRow> _detailRows =>
        _dashboard is null
            ? []
            : Trends
                .SelectMany(trend =>
                    trend.Points.Select(point => new {point.BucketStart, trend.MetricCode, point.Value}))
                .GroupBy(item => item.BucketStart)
                .OrderBy(group => group.Key)
                .Select(group => new PivotRow(
                    group.Key,
                    Trends
                        .Select(trend => group.FirstOrDefault(item => item.MetricCode == trend.MetricCode)?.Value)
                        .ToArray()))
                .ToArray();

    private sealed record PivotRow(DateOnly Date, decimal?[] Values);
}
