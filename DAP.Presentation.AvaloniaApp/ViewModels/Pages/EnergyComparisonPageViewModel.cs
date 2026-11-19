using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.AvaloniaApp.Common;
using DAP.Presentation.AvaloniaApp.Controls;
using DAP.Presentation.AvaloniaApp.Navigation;
using DAP.Presentation.AvaloniaApp.Services;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Pages;

/// <summary>
/// 班组横向对比：同一统计口径下，各班组的四项指标与综合能耗对比。
/// </summary>
public sealed partial class EnergyComparisonPageViewModel : AutoRefreshPageViewModelBase
{
    private static readonly string[] MetricAccentKeys =
        ["DapInfoBrush", "DapSuccessBrush", "DapWarningBrush", "DapTertiaryBrush"];

    private static readonly string[] MetricStrongKeys =
        ["DapInfoStrongBrush", "DapSuccessStrongBrush", "DapWarningStrongBrush", "DapTertiaryStrongBrush"];

    private static readonly string[] MetricTintKeys =
        ["DapInfoTintBrush", "DapSuccessTintBrush", "DapWarningTintBrush", "DapTertiaryTintBrush"];

    private readonly DesktopSession _session;

    /// <summary>
    /// 初始化对比页。
    /// </summary>
    /// <param name="session">桌面会话。</param>
    public EnergyComparisonPageViewModel(DesktopSession session)
    {
        _session = session;

        GranularityOptions =
        [
            new GranularityOption("day", "按日"),
            new GranularityOption("week", "按周"),
            new GranularityOption("month", "按月")
        ];

        SelectedGranularity = GranularityOptions[0];
        SelectedDate = DateTime.Today;
    }

    /// <inheritdoc />
    public override string PageKey => PageKeys.EnergyComparison;

    /// <inheritdoc />
    public override string Title => "班组横向对比";

    /// <inheritdoc />
    public override string Description => "同一统计口径下比较各班组的原辅材料与能源消耗，识别高低差。";

    /// <inheritdoc />
    public override string Accent => "warning";

    /// <summary>
    /// 统计粒度选项。
    /// </summary>
    public ObservableCollection<GranularityOption> GranularityOptions { get; }

    /// <summary>
    /// 当前统计粒度。
    /// </summary>
    [ObservableProperty] private GranularityOption? _selectedGranularity;

    /// <summary>
    /// 参考日期。
    /// </summary>
    [ObservableProperty] private DateTime? _selectedDate = DateTime.Today;

    /// <summary>
    /// 是否有数据。
    /// </summary>
    [ObservableProperty] private bool _hasData;

    /// <summary>
    /// 统计口径文案。
    /// </summary>
    [ObservableProperty] private string _rangeText = string.Empty;

    /// <summary>
    /// 概览卡。
    /// </summary>
    public ObservableCollection<MetricCardModel> SummaryCards { get; } = [];

    /// <summary>
    /// 柱状对比卡。
    /// </summary>
    public ObservableCollection<BarMetricCardModel> MetricCharts { get; } = [];

    /// <summary>
    /// 明细行。
    /// </summary>
    public ObservableCollection<TeamComparisonRowModel> TeamRows { get; } = [];

    /// <summary>
    /// 第一列指标名。
    /// </summary>
    [ObservableProperty] private string _column1 = "指标 1";

    /// <summary>
    /// 第二列指标名。
    /// </summary>
    [ObservableProperty] private string _column2 = "指标 2";

    /// <summary>
    /// 第三列指标名。
    /// </summary>
    [ObservableProperty] private string _column3 = "指标 3";

    /// <summary>
    /// 第四列指标名。
    /// </summary>
    [ObservableProperty] private string _column4 = "指标 4";

    /// <summary>
    /// 粒度变化后重新查询。
    /// </summary>
    partial void OnSelectedGranularityChanged(GranularityOption? value)
    {
        if (value is not null) _ = ReloadAsync();
    }

    /// <summary>
    /// 参考日期变化后重新查询。
    /// </summary>
    partial void OnSelectedDateChanged(DateTime? value)
    {
        if (value is not null) _ = ReloadAsync();
    }

    /// <inheritdoc />
    protected override async Task LoadCoreAsync()
    {
        GranularityOption granularity = SelectedGranularity ?? GranularityOptions[0];
        DateOnly reference = DateOnly.FromDateTime(SelectedDate ?? DateTime.Today);

        EnergyComparisonDto comparison =
            await _session.Api.GetEnergyComparisonAsync(granularity.Code, reference);

        Apply(comparison);
    }

    /// <summary>
    /// 铺数据。
    /// </summary>
    private void Apply(EnergyComparisonDto comparison)
    {
        EnergyMetricDescriptorDto[] metrics = comparison.Metrics.ToArray();
        TeamEnergyComparisonDto[] teams = comparison.Teams.ToArray();

        HasData = teams.Length > 0;
        RangeText = $"{comparison.Label} · {MetricFormat.Range(comparison.RangeStart, comparison.RangeEnd)}";

        SummaryCards.Clear();
        MetricCharts.Clear();
        TeamRows.Clear();

        if (teams.Length == 0) return;

        TeamEnergyComparisonDto highest = teams.MaxBy(team => team.TotalConsumption)!;
        TeamEnergyComparisonDto lowest = teams.MinBy(team => team.TotalConsumption)!;
        decimal spread = highest.TotalConsumption - lowest.TotalConsumption;

        SummaryCards.Add(new MetricCardModel("参与对比班组", teams.Length.ToString(), RangeText,
            NavIcons.Account, Palette.Brush("DapWarningBrush"), Palette.Brush("DapWarningStrongBrush"),
            Palette.Brush("DapWarningSoftBrush"), Palette.Brush("DapSurfaceBrush")));
        SummaryCards.Add(new MetricCardModel("综合能耗最高", highest.TeamName,
            $"{MetricFormat.Value(highest.TotalConsumption)} kgce，区间内最高",
            NavIcons.Insights, Palette.Brush("DapPrimaryBrush"), Palette.Brush("DapPrimaryStrongBrush"),
            Palette.Brush("DapPrimarySoftBrush"), Palette.Brush("DapSurfaceBrush")));
        SummaryCards.Add(new MetricCardModel("综合能耗最低", lowest.TeamName,
            $"{MetricFormat.Value(lowest.TotalConsumption)} kgce，区间内最低",
            NavIcons.BarChart, Palette.Brush("DapSuccessBrush"), Palette.Brush("DapSuccessStrongBrush"),
            Palette.Brush("DapSuccessSoftBrush"), Palette.Brush("DapSurfaceBrush")));
        SummaryCards.Add(new MetricCardModel("极差", MetricFormat.Value(spread), "最高与最低班组的综合能耗差值",
            NavIcons.Bolt, Palette.Brush("DapInfoBrush"), Palette.Brush("DapInfoStrongBrush"),
            Palette.Brush("DapInfoSoftBrush"), Palette.Brush("DapSurfaceBrush")));

        for (int index = 0; index < metrics.Length && index < 4; index++)
        {
            EnergyMetricDescriptorDto metric = metrics[index];

            MetricCharts.Add(new BarMetricCardModel(
                metric.MetricName,
                $"单位 {metric.Unit}，按{MetricFormat.GranularityLabel(comparison.Granularity)}对比",
                metric.Unit,
                $"{teams.Length} 个班组在 {metric.MetricName} 上的对比",
                teams.Select(team => new ChartSample(
                        team.TeamName,
                        team.MetricValues.FirstOrDefault(item => item.MetricCode == metric.MetricCode)?.Value ?? 0m))
                    .ToArray(),
                Palette.Brush(MetricAccentKeys[index]),
                Palette.Brush(MetricStrongKeys[index]),
                Palette.Brush("DapSurfaceBrush")));
        }

        Column1 = metrics.ElementAtOrDefault(0)?.MetricName ?? "指标 1";
        Column2 = metrics.ElementAtOrDefault(1)?.MetricName ?? "指标 2";
        Column3 = metrics.ElementAtOrDefault(2)?.MetricName ?? "指标 3";
        Column4 = metrics.ElementAtOrDefault(3)?.MetricName ?? "指标 4";

        foreach (TeamEnergyComparisonDto team in teams.OrderByDescending(team => team.TotalConsumption))
        {
            string badge = "";
            IBrush badgeAccent = Palette.Brush("DapTextSecondaryBrush");

            if (team.TeamCode == highest.TeamCode)
            {
                badge = "最高";
                badgeAccent = Palette.Brush("DapErrorStrongBrush");
            }
            else if (team.TeamCode == lowest.TeamCode)
            {
                badge = "最低";
                badgeAccent = Palette.Brush("DapSuccessStrongBrush");
            }

            TeamRows.Add(new TeamComparisonRowModel(
                team.TeamName,
                TeamValue(team, metrics.ElementAtOrDefault(0)),
                TeamValue(team, metrics.ElementAtOrDefault(1)),
                TeamValue(team, metrics.ElementAtOrDefault(2)),
                TeamValue(team, metrics.ElementAtOrDefault(3)),
                MetricFormat.Value(team.TotalConsumption),
                team.ShiftCount.ToString(),
                $"{team.TotalHours:0.##} h",
                badge,
                badgeAccent));
        }
    }

    /// <summary>
    /// 取班组在某项指标上的合计值。
    /// </summary>
    private static string TeamValue(TeamEnergyComparisonDto team, EnergyMetricDescriptorDto? metric)
    {
        if (metric is null) return "--";

        EnergyMetricValueDto? value =
            team.MetricValues.FirstOrDefault(item => item.MetricCode == metric.MetricCode);

        return value is null ? "--" : MetricFormat.Value(value.Value);
    }
}