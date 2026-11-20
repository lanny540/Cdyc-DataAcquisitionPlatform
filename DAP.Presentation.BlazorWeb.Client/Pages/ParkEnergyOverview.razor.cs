using DAP.Core.Shared.Contracts;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 园区能源总览。
///
/// 与「数据指标」里的钞纸班组能耗不是同一件事：那边按工序看（班组 × 四项指标 × 班次），
/// 这边按经营看（介质 × 组织 × 用途 × 系统）。因此主指标是综合能耗与介质结构，
/// 而不是某个班次用了多少度电。
///
/// 数据全部来自 <c>/api/park-energy/overview</c>。开发环境没有计量读数时，
/// 页面走空态并提供一个「示例数据」入口——示例必须由用户主动点开，
/// 且顶部有醒目提示，不能默认把假数渲染成实测的样子。
/// </summary>
public partial class ParkEnergyOverview
{
    private const string PeriodMonth = "month";
    private const string PeriodYear = "year";

    /// <summary>
    /// 指标卡图标。按指标键取，不按顺序取——一旦某个介质没有计量点、卡片少一张，
    /// 按顺序取图标会让「用水量」顶着电的图标。
    /// </summary>
    private static readonly Dictionary<string, string> KpiIcons = new(StringComparer.Ordinal)
    {
        ["totalTce"] = Icons.Material.Filled.EnergySavingsLeaf,
        ["elec"] = Icons.Material.Filled.ElectricBolt,
        ["water"] = Icons.Material.Filled.WaterDrop,
        ["steam"] = Icons.Material.Filled.Cloud
    };

    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;

    private ParkEnergyOverviewDto? _overview;
    private string _period = PeriodMonth;
    private bool _isLoading = true;
    private bool _demoMode;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;

    /// <summary>
    /// 是否处于「无读数」状态。加载中不算空态，否则首屏会先闪一下空状态再出数据。
    /// </summary>
    private bool IsEmpty =>
        !_isLoading && (_overview is null || _overview.DataStatus == ParkEnergyDataStatus.Empty);

    /// <summary>
    /// 是否为示例数据。
    /// </summary>
    private bool IsSample => _overview?.DataStatus == ParkEnergyDataStatus.Sample;

    /// <summary>
    /// 峰值与需量摘要。
    /// </summary>
    private string PeakSummary
    {
        get
        {
            if (_overview is null || _overview.LoadPoints.Count == 0)
            {
                return "暂无负荷数据";
            }

            ParkEnergyLoadPointDto peak = _overview.LoadPoints.MaxBy(point => point.LoadKw)!;

            return _overview.DeclaredDemandKw is { } declared
                ? $"峰值 {peak.LoadKw:N0} kW · {peak.Hour:00}:00 · 申报 {declared:N0} kW"
                : $"峰值 {peak.LoadKw:N0} kW · {peak.Hour:00}:00";
        }
    }

    /// <summary>
    /// 部门占比条形的归一化基准。按最大值归一，否则最大的部门也只有 42% 宽，条形看不出差异。
    /// </summary>
    private decimal MaxOrgShare =>
        _overview is null || _overview.OrgRows.Count == 0
            ? 1m
            : _overview.OrgRows.Max(row => row.Share);

    private string LastUpdatedText => _overview?.DataThrough is { } through
        ? $"数据截至 {through:MM-dd HH:mm}"
        : "暂无读数";

    /// <inheritdoc />
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
            _overview = await PlatformApiClient.GetParkEnergyOverviewAsync(_period, _demoMode);

            // 服务端给的是实测数据时，复位本地演示标记，
            // 否则页头会一直挂着「示例数据」标签，而数其实是真的。
            if (_overview.DataStatus != ParkEnergyDataStatus.Sample)
            {
                _demoMode = false;
            }

            if (_overview.DataStatus == ParkEnergyDataStatus.Empty && _overview.Warnings.Count > 0)
            {
                _message = _overview.Warnings.First();
                _messageSeverity = Severity.Info;
            }
        }
        catch (Exception ex)
        {
            _message = $"园区能源数据加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
            _overview = null;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task OnPeriodChangedAsync(string period)
    {
        if (string.Equals(_period, period, StringComparison.Ordinal))
        {
            return;
        }

        _period = period;
        await LoadAsync();
    }

    private async Task ToggleDemoAsync()
    {
        _demoMode = !_demoMode;
        await LoadAsync();
    }

    private static string KpiIcon(string key)
    {
        return KpiIcons.TryGetValue(key, out string? icon) ? icon : Icons.Material.Filled.Insights;
    }

    private static string KpiDeltaClass(bool? isImprovement)
    {
        return isImprovement switch
        {
            true => "park-kpi-delta--good",
            false => "park-kpi-delta--bad",
            // 无可比基准时不能用绿色或红色：那会让「没数据」看起来像「表现良好」。
            null => "park-kpi-delta--flat"
        };
    }

    private static string FormatDelta(decimal? delta)
    {
        return delta switch
        {
            null => "—",
            > 0 => $"↑ {delta:0.#}%",
            < 0 => $"↓ {Math.Abs(delta.Value):0.#}%",
            _ => "→ 0%"
        };
    }
}
