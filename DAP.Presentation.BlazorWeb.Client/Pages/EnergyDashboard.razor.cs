using DAP.Core.Shared.Contracts;
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
    /// 自动刷新开关。默认关闭：能耗页开着不动时不该持续打接口，
    /// 需要看板常驻的用户自己打开（见 <c>PlatformAutoRefresh</c> 注释）。
    /// </summary>
    private bool _autoRefreshEnabled;

    /// <summary>
    /// 自动刷新间隔（秒），与下拉档位一致。
    /// </summary>
    private int _refreshIntervalSeconds = 60;

    /// <summary>
    /// 最近一次成功加载时间，用于「数据截至 HH:mm:ss」。
    /// </summary>
    private DateTimeOffset? _lastUpdatedAt;

    /// <summary>
    /// 是否正在静默刷新。与 <see cref="_isLoading"/> 分开：静默刷新不换遮罩、不清数据。
    /// </summary>
    private bool _isRefreshing;

    /// <summary>
    /// 指标看板视图标识。
    /// </summary>
    private const int BoardView = 0;

    /// <summary>
    /// 数字孪生视图标识：钞纸机工艺线示意，数值由 <c>PaperMachineSimulator</c> 模拟。
    /// </summary>
    private const int DigitalTwinView = 1;

    /// <summary>
    /// 当前视图。默认落在指标看板，与用户进入大屏的目的（先看数）一致。
    /// </summary>
    private int _activeView = BoardView;

    private bool IsBoardView => _activeView == BoardView;

    /// <summary>
    /// 切换大屏视图。只切显示分支，不重新拉数据：
    /// 看板数据在切回时仍是切走前那一份，「数据截至」时间也保持连续。
    /// </summary>
    /// <param name="view">目标视图标识。</param>
    private void SwitchView(int view)
    {
        _activeView = view;
    }

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

    /// <summary>
    /// 是否严格解析出在岗班组。轮休 / 排班未导入 / 手动切换都算非在岗，
    /// 页面据此切换提示文案，绝不把猜测的班组当成在岗事实（docs/AUTH_DESIGN.md 6.6）。
    /// </summary>
    private bool IsOnDuty => _dashboard?.TeamResolution is "on-duty";

    /// <summary>
    /// 非在岗时的提示文案：轮休与排班未导入是两种不同的诉求，不能都写成"无权限"式的含糊话。
    /// </summary>
    private string? TeamResolutionHint => _dashboard?.TeamResolution switch
    {
        "rest-day" => "当前无在岗班组（当日轮休），已展示首个班组的数据，可在右上角手动切换。",
        "no-schedule" => "排班数据尚未导入，请联系排班管理员；当前展示的是参考数据。",
        _ => null
    };

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    /// <summary>
    /// 首载与手动刷新：显示整页遮罩，失败时清空数据（此时没有旧数据可保留）。
    /// </summary>
    private Task LoadAsync()
    {
        return LoadInternalAsync(false);
    }

    /// <summary>
    /// 定时刷新：不换遮罩、不清旧数据，失败只出一条警告提示。
    /// </summary>
    private Task RefreshSilentlyAsync()
    {
        return LoadInternalAsync(true);
    }

    /// <param name="silent">true 表示定时触发的静默刷新。</param>
    private async Task LoadInternalAsync(bool silent)
    {
        // 首载尚未结束时定时器可能已经跑到第一拍：两个请求并行会互相覆盖结果。
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
            _teams = await PlatformApiClient.GetEnergyTeamsAsync();

            _dashboard = await PlatformApiClient.GetEnergyDashboardAsync(_trendDays, _teamCode);

            // 首次加载后回填服务端解析出的班组，便于用户直接切换。
            // 非在岗解析（轮休/未导入）不回填默认值：让选择器保持空，提示文案引导手动选择。
            if (_teamCode is null && IsOnDuty)
            {
                _teamCode = _dashboard.TeamCode;
            }

            _lastUpdatedAt = DateTimeOffset.Now;
            _message = null;
        }
        catch (Exception ex)
        {
            // 静默刷新失败保留上一次成功的数据：定时器跑在用户没操作的时刻，
            // 一次瞬时抖动把看板清空比展示略旧的数据糟糕得多。
            _message = silent
                ? $"自动刷新未成功，页面仍显示上次加载的数据：{UserFacingErrors.Describe(ex)}"
                : $"能耗数据加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = silent ? Severity.Warning : Severity.Error;

            if (!silent)
            {
                _dashboard = null;
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
                Name = trend.MetricName, Data = trend.Points.Select(item => (double)item.Value).ToArray()
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

        int step = (int)Math.Ceiling(points.Count / (double)maxLabels);

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
                    trend.Points.Select(point => new { point.BucketStart, trend.MetricCode, point.Value }))
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
