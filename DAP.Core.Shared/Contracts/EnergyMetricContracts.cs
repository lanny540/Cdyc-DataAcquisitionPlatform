namespace DAP.Core.Shared.Contracts;

/// <summary>
/// 表示一个能耗指标的定义，对应一种原辅材料。
/// </summary>
/// <param name="MetricCode">指标编码。</param>
/// <param name="MetricName">指标名称。</param>
/// <param name="Unit">计量单位。</param>
/// <param name="Category">指标分类，例如原辅材料或能源。</param>
public sealed record EnergyMetricDescriptorDto(
    string MetricCode,
    string MetricName,
    string Unit,
    string Category);

/// <summary>
/// 表示某个班组在某个统计粒度上的一项指标数值。
/// </summary>
/// <param name="TeamCode">班组编码。</param>
/// <param name="TeamName">班组名称。</param>
/// <param name="MetricCode">指标编码。</param>
/// <param name="MetricName">指标名称。</param>
/// <param name="Unit">计量单位。</param>
/// <param name="BucketStart">统计区间起点。</param>
/// <param name="BucketEnd">统计区间终点。</param>
/// <param name="Value">指标数值。</param>
public sealed record EnergyMetricValueDto(
    string TeamCode,
    string TeamName,
    string MetricCode,
    string MetricName,
    string Unit,
    DateOnly BucketStart,
    DateOnly BucketEnd,
    decimal Value);

/// <summary>
/// 表示一条趋势序列上的数据点。
/// </summary>
/// <param name="BucketStart">统计区间起点。</param>
/// <param name="Label">横轴标签。</param>
/// <param name="Value">指标数值。</param>
public sealed record EnergyTrendPointDto(DateOnly BucketStart, string Label, decimal Value);

/// <summary>
/// 表示单个指标的趋势序列。
/// </summary>
/// <param name="MetricCode">指标编码。</param>
/// <param name="MetricName">指标名称。</param>
/// <param name="Unit">计量单位。</param>
/// <param name="Points">按时间升序排列的数据点。</param>
/// <param name="Total">区间内合计值。</param>
/// <param name="Average">区间内平均值。</param>
/// <param name="Peak">区间内最大值。</param>
/// <param name="PeakBucket">最大值所在区间的起点。</param>
/// <param name="TrendDirection">趋势方向，取值为 up、down 或 flat。</param>
public sealed record EnergyTrendDto(
    string MetricCode,
    string MetricName,
    string Unit,
    IReadOnlyCollection<EnergyTrendPointDto> Points,
    decimal Total,
    decimal Average,
    decimal Peak,
    DateOnly? PeakBucket,
    string TrendDirection);

/// <summary>
/// 表示当前班组的能耗总览数据。
/// </summary>
/// <param name="TeamCode">班组编码。</param>
/// <param name="TeamName">班组名称。</param>
/// <param name="WorkDate">数据所属自然日。</param>
/// <param name="ShiftName">数据所属班次。</param>
/// <param name="ShiftStart">班次开始时间。</param>
/// <param name="ShiftEnd">班次结束时间。</param>
/// <param name="TotalConsumption">四项指标折算后的综合能耗合计。</param>
/// <param name="TotalUnit">综合能耗单位。</param>
/// <param name="MetricTotals">四项指标在当前班次的合计。</param>
/// <param name="Trends">四项指标的历史趋势。</param>
/// <param name="TeamResolution">班组归属的解析方式：on-duty 严格命中当天排班且当前时刻在班次内；
/// manual 用户手动指定；rest-day 当天轮休或无匹配班次（不给在岗默认值）；no-schedule 排班未导入。
/// 车间终端把默认值当在岗事实，因此 rest-day / no-schedule 必须在页面上显式提示（docs/AUTH_DESIGN.md 6.6）。</param>
public sealed record EnergyDashboardDto(
    string TeamCode,
    string TeamName,
    DateOnly WorkDate,
    string ShiftName,
    TimeOnly ShiftStart,
    TimeOnly ShiftEnd,
    decimal TotalConsumption,
    string TotalUnit,
    IReadOnlyCollection<EnergyMetricValueDto> MetricTotals,
    IReadOnlyCollection<EnergyTrendDto> Trends,
    string TeamResolution = "on-duty");

/// <summary>
/// 表示班组历史指标查询结果。
/// </summary>
/// <param name="TeamCode">班组编码。</param>
/// <param name="TeamName">班组名称。</param>
/// <param name="Granularity">统计粒度，取值为 day、week 或 month。</param>
/// <param name="RangeStart">区间起始自然日。</param>
/// <param name="RangeEnd">区间结束自然日。</param>
/// <param name="Metrics">本次查询涉及的指标定义。</param>
/// <param name="Trends">每个指标的趋势序列。</param>
/// <param name="DetailRows">按粒度展开的明细行。</param>
public sealed record EnergyHistoryDto(
    string TeamCode,
    string TeamName,
    string Granularity,
    DateOnly RangeStart,
    DateOnly RangeEnd,
    IReadOnlyCollection<EnergyMetricDescriptorDto> Metrics,
    IReadOnlyCollection<EnergyTrendDto> Trends,
    IReadOnlyCollection<EnergyMetricValueDto> DetailRows);

/// <summary>
/// 表示一个班组在指定区间内的横向对比汇总。
/// </summary>
/// <param name="TeamCode">班组编码。</param>
/// <param name="TeamName">班组名称。</param>
/// <param name="MetricValues">该班组各项指标的合计值。</param>
/// <param name="TotalConsumption">该班组的综合能耗合计。</param>
/// <param name="ShiftCount">区间内的出勤班次数量。</param>
/// <param name="TotalHours">区间内的出勤工时。</param>
public sealed record TeamEnergyComparisonDto(
    string TeamCode,
    string TeamName,
    IReadOnlyCollection<EnergyMetricValueDto> MetricValues,
    decimal TotalConsumption,
    int ShiftCount,
    double TotalHours);

/// <summary>
/// 表示所有班组在指定统计口径下的横向对比结果。
/// </summary>
/// <param name="Granularity">统计粒度，取值为 day、week 或 month。</param>
/// <param name="RangeStart">区间起始自然日。</param>
/// <param name="RangeEnd">区间结束自然日。</param>
/// <param name="Label">口径名称，例如 2026-10-20 或 2026-W43。</param>
/// <param name="Metrics">参与对比的指标定义。</param>
/// <param name="Teams">各班组的对比数据。</param>
public sealed record EnergyComparisonDto(
    string Granularity,
    DateOnly RangeStart,
    DateOnly RangeEnd,
    string Label,
    IReadOnlyCollection<EnergyMetricDescriptorDto> Metrics,
    IReadOnlyCollection<TeamEnergyComparisonDto> Teams);
