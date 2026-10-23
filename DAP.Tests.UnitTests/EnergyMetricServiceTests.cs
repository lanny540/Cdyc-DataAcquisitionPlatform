using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 能耗指标服务测试。
///
/// 现阶段指标由确定性算法生成，核心契约是「同一输入恒定输出」——
/// 否则用户每次刷新页面都会看到不同的数值，无法据此判断趋势。
/// </summary>
public class EnergyMetricServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    private static DeterministicEnergyMetricService CreateService(params ShiftScheduleRowDto[] rows)
    {
        var shiftService = new InMemoryShiftSchedulingService();
        return new DeterministicEnergyMetricService(shiftService, new FixedTimeProvider(Today));
    }

    [Fact]
    public async Task GetDashboardAsync_Should_Return_Four_Metrics_And_Their_Trends()
    {
        DeterministicEnergyMetricService service = CreateService();

        EnergyDashboardDto dashboard = await service.GetDashboardAsync(7);

        Assert.Equal(4, dashboard.MetricTotals.Count);
        Assert.Equal(4, dashboard.Trends.Count);

        // 每条趋势应与查询天数一致，且点数为 7 天。
        Assert.All(dashboard.Trends, trend =>
        {
            Assert.Equal(7, trend.Points.Count);
            Assert.NotEmpty(trend.Unit);
        });

        Assert.Equal("kgce", dashboard.TotalUnit);
        Assert.True(dashboard.TotalConsumption > 0m);
    }

    [Fact]
    public async Task GetDashboardAsync_Should_Return_Same_Values_For_Repeated_Calls()
    {
        DeterministicEnergyMetricService service = CreateService();

        EnergyDashboardDto first = await service.GetDashboardAsync(14);
        EnergyDashboardDto second = await service.GetDashboardAsync(14);

        Assert.Equal(first.TotalConsumption, second.TotalConsumption);
        Assert.Equal(first.TeamCode, second.TeamCode);

        foreach ((EnergyTrendDto left, EnergyTrendDto right) in first.Trends.Zip(second.Trends))
        {
            Assert.Equal(left.Total, right.Total);
            Assert.Equal(
                left.Points.Select(point => point.Value).ToArray(),
                right.Points.Select(point => point.Value).ToArray());
        }
    }

    [Fact]
    public async Task GetDashboardAsync_Should_Pick_Different_Values_For_Different_Teams()
    {
        DeterministicEnergyMetricService service = CreateService();

        EnergyDashboardDto teamA = await service.GetDashboardAsync(7, "TEAM-甲");
        EnergyDashboardDto teamB = await service.GetDashboardAsync(7, "TEAM-乙");

        Assert.NotEqual(teamA.TotalConsumption, teamB.TotalConsumption);
    }

    [Fact]
    public async Task GetComparisonAsync_Should_Return_Every_Team_With_Comparable_Totals()
    {
        DeterministicEnergyMetricService service = CreateService();

        EnergyComparisonDto comparison = await service.GetComparisonAsync("day", Today);

        Assert.Equal("day", comparison.Granularity);
        Assert.NotEmpty(comparison.Teams);

        TeamEnergyComparisonDto first = comparison.Teams.First();
        Assert.Equal(4, first.MetricValues.Count);
        Assert.True(first.TotalConsumption > 0m);

        // 最高与最低必须是真实存在的两个班组（数据多于 1 个班组时）。
        if (comparison.Teams.Count > 1)
        {
            Assert.NotEqual(
                comparison.Teams.Max(item => item.TotalConsumption),
                comparison.Teams.Min(item => item.TotalConsumption));
        }
    }

    [Fact]
    public async Task GetComparisonAsync_Should_Normalize_Month_To_Calendar_Month()
    {
        DeterministicEnergyMetricService service = CreateService();

        EnergyComparisonDto comparison = await service.GetComparisonAsync("month", new DateOnly(2026, 2, 10));

        Assert.Equal("month", comparison.Granularity);
        Assert.Equal(new DateOnly(2026, 2, 1), comparison.RangeStart);
        Assert.Equal(new DateOnly(2026, 2, 28), comparison.RangeEnd);
    }

    [Fact]
    public async Task GetHistoryAsync_Should_Respect_Granularity_And_Return_Pivotable_Detail()
    {
        DeterministicEnergyMetricService service = CreateService();

        EnergyHistoryDto history = await service.GetHistoryAsync("TEAM-甲", "day", Today.AddDays(-6), Today);

        Assert.Equal("day", history.Granularity);
        Assert.Equal(4, history.Trends.Count);

        // 明细行数应等于「趋势序列数 × 桶数」。
        var expectedRows = history.Trends.Sum(trend => trend.Points.Count);
        Assert.Equal(expectedRows, history.DetailRows.Count);
    }

    [Fact]
    public async Task GetHistoryAsync_Should_Not_Distort_Totals_When_Bucketing()
    {
        DeterministicEnergyMetricService service = CreateService();
        var team = "TEAM-甲";

        EnergyHistoryDto fine = await service.GetHistoryAsync(team, "day", Today.AddDays(-6), Today);
        EnergyHistoryDto longRange =
            await service.GetHistoryAsync(team, "day", Today.AddDays(-29), Today);

        // 桶宽不一致会让最后一个桶吞掉整段区间，导致曲线末端出现虚假的归零跌落，
        // 且区间合计与逐日相加不相等。这里断言两个量级可比较且无异常零值。
        Assert.All(longRange.Trends, trend =>
        {
            Assert.DoesNotContain(0m, trend.Points.Select(point => point.Value));
            Assert.True(trend.Points.Count <= 31);
        });

        Assert.True(
            fine.Trends.First().Total < longRange.Trends.First().Total,
            "30 天区间的合计应大于 7 天区间的合计。");
    }

    [Fact]
    public async Task GetHistoryAsync_Should_Cap_Bucket_Count_For_Long_Ranges()
    {
        DeterministicEnergyMetricService service = CreateService();

        EnergyHistoryDto history = await service.GetHistoryAsync("TEAM-甲", "day",
            Today.AddYears(-3), Today);

        // 长期区间必须收敛，避免一次生成上万个数据点拖垮浏览器。
        Assert.All(history.Trends, trend => Assert.True(trend.Points.Count <= 400));
    }

    [Fact]
    public async Task GetMetricsAsync_Should_Expose_Four_Descriptors_With_Units()
    {
        DeterministicEnergyMetricService service = CreateService();

        IReadOnlyCollection<EnergyMetricDescriptorDto> metrics = await service.GetMetricsAsync();

        Assert.Equal(4, metrics.Count);
        Assert.Equal(4, metrics.Select(metric => metric.MetricCode).Distinct().Count());
        Assert.All(metrics, metric => Assert.False(string.IsNullOrWhiteSpace(metric.Unit)));
    }

    /// <summary>
    /// 固定时间的 TimeProvider，避免测试随系统日期漂移。
    /// </summary>
    private sealed class FixedTimeProvider(DateOnly date) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 30)), TimeSpan.FromHours(8));
        }

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
