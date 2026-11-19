using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.AvaloniaApp.Common;
using DAP.Presentation.AvaloniaApp.Navigation;
using DAP.Presentation.AvaloniaApp.Services;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Pages;

/// <summary>
/// 能耗仪表板：上排汇总当前班组的四项指标，下排逐项展示趋势，末尾给出可核对的明细。
/// </summary>
public sealed partial class EnergyDashboardPageViewModel : EnergyPageViewModelBase
{
    /// <summary>
    /// 四项指标依次取用的语义色资源键与图标，与 Web 端保持一致。
    /// Strong 档用于标签/图标前景与图表系列，Soft 档用于图标底块。
    /// </summary>
    private static readonly string[] MetricAccentKeys =
        ["DapInfoBrush", "DapSuccessBrush", "DapWarningBrush", "DapTertiaryBrush"];

    private static readonly string[] MetricStrongKeys =
        ["DapInfoStrongBrush", "DapSuccessStrongBrush", "DapWarningStrongBrush", "DapTertiaryStrongBrush"];

    private static readonly string[] MetricSoftKeys =
        ["DapInfoSoftBrush", "DapSuccessSoftBrush", "DapWarningSoftBrush", "DapTertiarySoftBrush"];

    private static readonly string[] MetricTintKeys =
        ["DapInfoTintBrush", "DapSuccessTintBrush", "DapWarningTintBrush", "DapTertiaryTintBrush"];

    private static readonly string[] MetricIcons =
        [NavIcons.Science, NavIcons.Opacity, NavIcons.Grain, NavIcons.Bolt];

    private bool _isApplyingDashboard;

    /// <summary>
    /// 初始化仪表板。
    /// </summary>
    /// <param name="session">桌面会话。</param>
    public EnergyDashboardPageViewModel(DesktopSession session) : base(session)
    {
        TrendDayOptions = new ObservableCollection<int>([3, 7, 14, 30]);
    }

    /// <inheritdoc />
    public override string PageKey => PageKeys.EnergyDashboard;

    /// <inheritdoc />
    public override string Title => "能耗仪表板";

    /// <inheritdoc />
    public override string Description => "当前班组 4 项原辅材料与能源指标合计、历史趋势与明细。";

    /// <inheritdoc />
    public override string Accent => "success";

    /// <summary>
    /// 可选趋势天数。
    /// </summary>
    public ObservableCollection<int> TrendDayOptions { get; }

    /// <summary>
    /// 趋势天数。
    /// </summary>
    [ObservableProperty] private int _trendDays = 7;

    /// <summary>
    /// 是否已有数据可展示。
    /// </summary>
    [ObservableProperty] private bool _hasData;

    /// <summary>
    /// 班组徽标文案。
    /// </summary>
    [ObservableProperty] private string _teamChipText = string.Empty;

    /// <summary>
    /// 日期与班次徽标文案。
    /// </summary>
    [ObservableProperty] private string _shiftChipText = string.Empty;

    /// <summary>
    /// 班次时间段徽标文案。
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasShiftRange))]
    private string _shiftRangeText = string.Empty;

    /// <summary>
    /// 是否严格解析出在岗班组。轮休 / 未导入 / 手动切换都算非在岗。
    /// </summary>
    [ObservableProperty] private bool _isOnDuty;

    /// <summary>
    /// 非在岗时的说明文案，避免把猜测的班组当成在岗事实。
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasResolutionHint))]
    private string? _resolutionHint;

    /// <summary>
    /// 是否有班次时间段可展示。
    /// </summary>
    public bool HasShiftRange => !string.IsNullOrWhiteSpace(ShiftRangeText);

    /// <summary>
    /// 是否有班组归属提示。
    /// </summary>
    public bool HasResolutionHint => !string.IsNullOrWhiteSpace(ResolutionHint);

    /// <summary>
    /// 综合能耗文本。
    /// </summary>
    [ObservableProperty] private string _totalText = "--";

    /// <summary>
    /// 综合能耗单位。
    /// </summary>
    [ObservableProperty] private string _totalUnit = string.Empty;

    /// <summary>
    /// 指标统计卡。
    /// </summary>
    public ObservableCollection<MetricCardModel> MetricCards { get; } = [];

    /// <summary>
    /// 指标趋势卡。
    /// </summary>
    public ObservableCollection<TrendCardModel> Trends { get; } = [];

    /// <summary>
    /// 明细行。
    /// </summary>
    public ObservableCollection<PivotRowModel> DetailRows { get; } = [];

    /// <summary>
    /// 明细表头：指标名称，固定四列。
    /// </summary>
    [ObservableProperty] private string _column1 = "指标 1";

    /// <summary>
    /// 第二列。
    /// </summary>
    [ObservableProperty] private string _column2 = "指标 2";

    /// <summary>
    /// 第三列。
    /// </summary>
    [ObservableProperty] private string _column3 = "指标 3";

    /// <summary>
    /// 第四列。
    /// </summary>
    [ObservableProperty] private string _column4 = "指标 4";

    /// <summary>
    /// 趋势天数变化后重新查询。
    /// </summary>
    partial void OnTrendDaysChanged(int value)
    {
        if (!_isApplyingDashboard) _ = ReloadAsync();
    }

    /// <inheritdoc />
    protected override async Task LoadCoreAsync()
    {
        await LoadTeamsAsync();

        if (!HasTeams)
        {
            Reset();
            SetMessage("尚未导入班组排班数据，无法解析班组归属。请先在 Web 端「业务配置 → 排班时间管理」中导入排班表。");
            return;
        }

        EnergyDashboardDto dashboard =
            await Api.GetEnergyDashboardAsync(TrendDays, SelectedTeam?.Code);

        Apply(dashboard);
    }

    /// <summary>
    /// 把服务端结果铺到界面模型上。
    /// </summary>
    /// <param name="dashboard">仪表板数据。</param>
    private void Apply(EnergyDashboardDto dashboard)
    {
        _isApplyingDashboard = true;

        try
        {
            HasData = true;
            IsOnDuty = dashboard.TeamResolution is "on-duty";

            ResolutionHint = dashboard.TeamResolution switch
            {
                "rest-day" => "当前无在岗班组（当日轮休），已展示参考班组的数据，可在右上角手动切换。",
                "no-schedule" => "排班数据尚未导入，当前展示的是参考数据。",
                _ => null
            };

            TeamChipText = IsOnDuty
                ? $"当前班组：{dashboard.TeamName}"
                : $"查看班组：{dashboard.TeamName}（非在岗）";

            ShiftChipText = $"{dashboard.WorkDate:yyyy-MM-dd} {dashboard.ShiftName}";

            ShiftRangeText = dashboard.ShiftEnd > dashboard.ShiftStart
                ? $"{dashboard.ShiftStart:HH\\:mm} - {dashboard.ShiftEnd:HH\\:mm}"
                : string.Empty;

            TotalText = MetricFormat.Total(dashboard.TotalConsumption);
            TotalUnit = dashboard.TotalUnit;

            // 在岗时回填班组选择器，便于用户直接切换；非在岗不回填，避免把猜测的班组当成事实。
            if (IsOnDuty) SelectTeam(dashboard.TeamCode);

            BuildMetricCards(dashboard.MetricTotals.ToArray());
            BuildTrends(dashboard.Trends.ToArray(), dashboard.TeamName);
            BuildDetailRows(dashboard.Trends.ToArray());
        }
        finally
        {
            _isApplyingDashboard = false;
        }
    }

    /// <summary>
    /// 构建四张指标卡。
    /// </summary>
    private void BuildMetricCards(EnergyMetricValueDto[] totals)
    {
        MetricCards.Clear();

        for (int index = 0; index < totals.Length && index < 4; index++)
        {
            EnergyMetricValueDto total = totals[index];

            MetricCards.Add(new MetricCardModel(
                total.MetricName,
                MetricFormat.Value(total.Value),
                $"单位 {total.Unit}，{total.BucketStart:yyyy-MM-dd} 当班累计",
                MetricIcons[index],
                Palette.Brush(MetricAccentKeys[index]),
                Palette.Brush(MetricStrongKeys[index]),
                Palette.Brush(MetricSoftKeys[index]),
                Palette.Brush("DapSurfaceBrush")));
        }
    }

    /// <summary>
    /// 构建趋势卡与明细行。
    /// </summary>
    private void BuildTrends(EnergyTrendDto[] trends, string teamName)
    {
        Trends.Clear();

        for (int index = 0; index < trends.Length && index < 4; index++)
        {
            EnergyTrendDto trend = trends[index];

            Trends.Add(new TrendCardModel(
                trend.MetricName,
                trend.Unit,
                $"{teamName} 近 {TrendDays} 天，单位 {trend.Unit}",
                $"合计 {MetricFormat.Value(trend.Total)}",
                $"均值 {MetricFormat.Value(trend.Average)}",
                MetricFormat.TrendLabel(trend.TrendDirection),
                trend.TrendDirection switch
                {
                    "up" => Palette.Brush("DapErrorStrongBrush"),
                    "down" => Palette.Brush("DapSuccessStrongBrush"),
                    _ => Palette.Brush("DapTextSecondaryBrush")
                },
                MetricFormat.Samples(trend),
                Palette.Brush(MetricAccentKeys[index]),
                Palette.Brush(MetricStrongKeys[index]),
                Palette.Brush("DapSurfaceBrush")));
        }
    }

    /// <summary>
    /// 明细按自然日合并四项指标，与图表横轴同源。
    /// </summary>
    private void BuildDetailRows(EnergyTrendDto[] trends)
    {
        DetailRows.Clear();

        Column1 = trends.ElementAtOrDefault(0)?.MetricName ?? "指标 1";
        Column2 = trends.ElementAtOrDefault(1)?.MetricName ?? "指标 2";
        Column3 = trends.ElementAtOrDefault(2)?.MetricName ?? "指标 3";
        Column4 = trends.ElementAtOrDefault(3)?.MetricName ?? "指标 4";

        IEnumerable<IGrouping<DateOnly, (DateOnly Bucket, string Code, decimal Value)>> groups = trends
            .SelectMany(trend => trend.Points.Select(point => (point.BucketStart, trend.MetricCode, point.Value)))
            .GroupBy(item => item.BucketStart)
            .OrderBy(group => group.Key);

        foreach (IGrouping<DateOnly, (DateOnly Bucket, string Code, decimal Value)> group in groups)
            DetailRows.Add(new PivotRowModel(
                group.Key.ToString("yyyy-MM-dd"),
                ValueOf(group, trends.ElementAtOrDefault(0)),
                ValueOf(group, trends.ElementAtOrDefault(1)),
                ValueOf(group, trends.ElementAtOrDefault(2)),
                ValueOf(group, trends.ElementAtOrDefault(3))));
    }

    /// <summary>
    /// 取某个指标在某个周期上的数值。
    /// </summary>
    private static string ValueOf(
        IEnumerable<(DateOnly Bucket, string Code, decimal Value)> group,
        EnergyTrendDto? trend)
    {
        if (trend is null) return "--";

        foreach ((DateOnly _, string code, decimal value) in group)
            if (code == trend.MetricCode)
                return MetricFormat.Value(value);

        return "--";
    }

    /// <summary>
    /// 清空结果（排班未导入时不留上一轮的旧数据）。
    /// </summary>
    private void Reset()
    {
        HasData = false;
        MetricCards.Clear();
        Trends.Clear();
        DetailRows.Clear();
        TeamChipText = string.Empty;
        ShiftChipText = string.Empty;
        ShiftRangeText = string.Empty;
        ResolutionHint = null;
        TotalText = "--";
    }
}