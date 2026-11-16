using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.AvaloniaApp.Common;
using DAP.Presentation.AvaloniaApp.Navigation;
using DAP.Presentation.AvaloniaApp.Services;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Pages;

/// <summary>
/// 历史指标：按日、周、月查看单个班组的四项指标趋势与透视明细。
/// </summary>
public sealed partial class EnergyHistoryPageViewModel : EnergyPageViewModelBase
{
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

    /// <summary>
    /// 初始化历史指标页。
    /// </summary>
    /// <param name="session">桌面会话。</param>
    public EnergyHistoryPageViewModel(DesktopSession session) : base(session)
    {
        GranularityOptions =
        [
            new GranularityOption("day", "按日"),
            new GranularityOption("week", "按周"),
            new GranularityOption("month", "按月")
        ];

        SelectedGranularity = GranularityOptions[0];
    }

    /// <inheritdoc />
    public override string PageKey => PageKeys.EnergyHistory;

    /// <inheritdoc />
    public override string Title => "历史指标";

    /// <inheritdoc />
    public override string Description => "按日、周、月查看班组能耗趋势，明细与图表同源可核对。";

    /// <inheritdoc />
    public override string Accent => "tertiary";

    /// <summary>
    /// 统计粒度选项。
    /// </summary>
    public ObservableCollection<GranularityOption> GranularityOptions { get; }

    /// <summary>
    /// 当前统计粒度。
    /// </summary>
    [ObservableProperty]
    private GranularityOption? _selectedGranularity;

    /// <summary>
    /// 是否有数据。
    /// </summary>
    [ObservableProperty]
    private bool _hasData;

    /// <summary>
    /// 查询区间文案。
    /// </summary>
    [ObservableProperty]
    private string _rangeText = string.Empty;

    /// <summary>
    /// 数据点总数文案。
    /// </summary>
    [ObservableProperty]
    private string _pointCountText = string.Empty;

    /// <summary>
    /// 明细行数文案。
    /// </summary>
    [ObservableProperty]
    private string _detailCountText = string.Empty;

    /// <summary>
    /// 均值统计卡。
    /// </summary>
    public ObservableCollection<MetricCardModel> MetricCards { get; } = [];

    /// <summary>
    /// 趋势卡。
    /// </summary>
    public ObservableCollection<TrendCardModel> Trends { get; } = [];

    /// <summary>
    /// 透视明细行。
    /// </summary>
    public ObservableCollection<PivotRowModel> PivotRows { get; } = [];

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
    /// 周期列标题（跟随粒度变化）。
    /// </summary>
    [ObservableProperty]
    private string _periodHeader = "统计周期";

    /// <summary>
    /// 粒度变化后重新查询。
    /// </summary>
    partial void OnSelectedGranularityChanged(GranularityOption? value)
    {
        if (value is not null)
        {
            _ = ReloadAsync();
        }
    }

    /// <inheritdoc />
    protected override async Task LoadCoreAsync()
    {
        await LoadTeamsAsync();

        if (!HasTeams || SelectedTeam is null)
        {
            Reset();
            SetMessage("尚未导入班组排班数据，无法查询历史指标。请先在 Web 端导入排班表。");
            return;
        }

        GranularityOption granularity = SelectedGranularity ?? GranularityOptions[0];
        (DateOnly from, DateOnly to) = ResolveRange(granularity.Code);

        EnergyHistoryDto history = await Api.GetEnergyHistoryAsync(
            SelectedTeam.Code,
            granularity.Code,
            from,
            to);

        Apply(history, granularity);
    }

    /// <summary>
    /// 按粒度推导查询区间：月粒度取近 12 个月，避免一次拉取过多数据点。
    /// </summary>
    private static (DateOnly From, DateOnly To) ResolveRange(string granularity)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        return granularity switch
        {
            "week" => (today.AddDays(-83), today),
            "month" => (today.AddMonths(-12), today),
            _ => (today.AddDays(-29), today)
        };
    }

    /// <summary>
    /// 铺数据。
    /// </summary>
    private void Apply(EnergyHistoryDto history, GranularityOption granularity)
    {
        EnergyTrendDto[] trends = history.Trends.ToArray();

        HasData = trends.Length > 0;
        RangeText = MetricFormat.Range(history.RangeStart, history.RangeEnd);
        PeriodHeader = granularity.Label;
        PointCountText = $"共 {trends.Sum(trend => trend.Points.Count)} 个数据点";
        DetailCountText = $"{history.DetailRows.Count} 行记录，与图表同源";

        MetricCards.Clear();
        Trends.Clear();

        for (var index = 0; index < trends.Length && index < 4; index++)
        {
            EnergyTrendDto trend = trends[index];

            MetricCards.Add(new MetricCardModel(
                trend.MetricName,
                MetricFormat.Value(trend.Average),
                $"区间均值，峰值 {MetricFormat.Value(trend.Peak)} {trend.Unit}",
                MetricIcons[index],
                Palette.Brush(MetricAccentKeys[index]),
                Palette.Brush(MetricStrongKeys[index]),
                Palette.Brush(MetricSoftKeys[index]),
                Palette.Brush("DapSurfaceBrush")));

            Trends.Add(new TrendCardModel(
                $"{trend.MetricName}趋势",
                trend.Unit,
                $"{history.TeamName} 按{MetricFormat.GranularityLabel(history.Granularity)}统计，单位 {trend.Unit}",
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

        Column1 = trends.ElementAtOrDefault(0)?.MetricName ?? "指标 1";
        Column2 = trends.ElementAtOrDefault(1)?.MetricName ?? "指标 2";
        Column3 = trends.ElementAtOrDefault(2)?.MetricName ?? "指标 3";
        Column4 = trends.ElementAtOrDefault(3)?.MetricName ?? "指标 4";

        BuildPivotRows(trends);
    }

    /// <summary>
    /// 把长表按统计周期转成宽表，一行一个周期，四列对应四项指标。
    /// </summary>
    private void BuildPivotRows(EnergyTrendDto[] trends)
    {
        PivotRows.Clear();

        (DateOnly BucketStart, string Label, string MetricCode, decimal Value)[] entries = trends
            .SelectMany(trend => trend.Points.Select(point =>
                (point.BucketStart, point.Label, trend.MetricCode, point.Value)))
            .ToArray();

        foreach (IGrouping<DateOnly, (DateOnly BucketStart, string Label, string MetricCode, decimal Value)> group
                 in entries.GroupBy(entry => entry.BucketStart).OrderBy(entry => entry.Key))
        {
            var label = group.First().Label;

            PivotRows.Add(new PivotRowModel(
                label,
                Lookup(group, trends.ElementAtOrDefault(0)),
                Lookup(group, trends.ElementAtOrDefault(1)),
                Lookup(group, trends.ElementAtOrDefault(2)),
                Lookup(group, trends.ElementAtOrDefault(3))));
        }
    }

    /// <summary>
    /// 取指定指标在某个周期上的数值。
    /// </summary>
    private static string Lookup(
        IEnumerable<(DateOnly BucketStart, string Label, string MetricCode, decimal Value)> group,
        EnergyTrendDto? trend)
    {
        if (trend is null)
        {
            return "--";
        }

        foreach ((DateOnly _, string _, string metricCode, decimal value) in group)
        {
            if (metricCode == trend.MetricCode)
            {
                return MetricFormat.Value(value);
            }
        }

        return "--";
    }

    /// <summary>
    /// 清空结果。
    /// </summary>
    private void Reset()
    {
        HasData = false;
        MetricCards.Clear();
        Trends.Clear();
        PivotRows.Clear();
        RangeText = string.Empty;
        PointCountText = string.Empty;
        DetailCountText = string.Empty;
    }
}
