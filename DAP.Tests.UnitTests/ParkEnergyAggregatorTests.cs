using DAP.Core.Domain.Services;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 园区能源聚合口径测试。
///
/// 报表出错几乎都出在这几条口径上，而且错得都不明显：月初少算一天、
/// 换表算出一笔巨额能耗、二次能源被折了两次标。这些在页面上完全看不出来，
/// 只能靠这里的断言钉住。
/// </summary>
public sealed class ParkEnergyAggregatorTests
{
    /// <summary>园区时区，与生产默认值一致。</summary>
    private static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    /// <summary>统计期间固定为 2026 年 11 月 1 日至 11 月 30 日，便于构造跨月读数。</summary>
    private static readonly ParkEnergyPeriod Period =
        new(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));

    private static DateTimeOffset At(int day, int hour = 12) =>
        new(2026, 11, day, hour, 0, 0, Offset);

    /// <summary>上个月的同一天，用来构造「期间前基线」。</summary>
    private static DateTimeOffset Before(int day, int hour = 12) =>
        new(2026, 10, day, hour, 0, 0, Offset);

    /// <summary>默认组织节点：钞纸车间。</summary>
    private static readonly Guid PaperOrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ParkEnergyMeterInput Meter(
        string mediumCode = "ELEC",
        decimal? factor = 0.1229m,
        bool isPrimary = true,
        bool cumulative = true,
        decimal ratio = 1m,
        Guid? orgId = null,
        string orgName = "钞纸车间",
        string name = "1# 电表") =>
        new(
            Guid.NewGuid(),
            $"M-{name}",
            name,
            mediumCode,
            mediumCode == "ELEC" ? "电" : mediumCode,
            mediumCode == "ELEC" ? "kWh" : "t",
            isPrimary,
            factor,
            cumulative,
            ratio,
            orgId ?? PaperOrgId,
            orgName);

    /// <summary>
    /// 累计表：用量等于末次示数减基线示数，且基线取期间前最后一条。
    /// 少了这条，每月 1 日的报表会把整月前半段的用量整段丢掉。
    /// </summary>
    [Fact]
    public void Cumulative_Meter_Should_Diff_Against_Pre_Period_Baseline()
    {
        var meter = Meter();

        var readings = new[]
        {
            new ParkEnergyReadingInput(meter.Id, Before(28), 900m),
            new ParkEnergyReadingInput(meter.Id, At(2), 1000m),
            new ParkEnergyReadingInput(meter.Id, At(5), 1400m)
        };

        var result = ParkEnergyAggregator.Aggregate([meter], readings, Period, Offset);

        var usage = Assert.Single(result.Mediums);
        // (1400 - 900) 而不是 (1400 - 1000)：基线取 10 月 28 日那条。
        Assert.Equal(500m, usage.Quantity);
        // 500 kWh × 0.1229 kgce/kWh = 0.06145 tce，聚合层保留 4 位小数。
        Assert.NotNull(usage.CoalEquivalentTce);
        Assert.InRange(usage.CoalEquivalentTce!.Value, 0.0614m, 0.0615m);
        Assert.True(result.HasReadings);
    }

    /// <summary>
    /// 期间内只有一条读数且期间前没有任何基线时，用量是未知，不是 0。
    /// 显示成 0 会让人以为这个月没用电。
    /// </summary>
    [Fact]
    public void Single_Reading_Without_Baseline_Should_Be_Reported_Not_Zero()
    {
        var meter = Meter();

        var result = ParkEnergyAggregator.Aggregate(
            [meter],
            [new ParkEnergyReadingInput(meter.Id, At(3), 1200m)],
            Period,
            Offset);

        Assert.Empty(result.Mediums);
        Assert.Equal(0m, result.TotalTce);
        Assert.True(result.HasReadings);
        Assert.Contains(result.Warnings, warning => warning.Contains("读数不足"));
    }

    /// <summary>
    /// 期间内只有一条读数、但期间前有基线时，用量可以算出来。
    /// </summary>
    [Fact]
    public void Single_Reading_With_Baseline_Should_Produce_Usage()
    {
        var meter = Meter();

        var result = ParkEnergyAggregator.Aggregate(
            [meter],
            [
                new ParkEnergyReadingInput(meter.Id, Before(29), 800m),
                new ParkEnergyReadingInput(meter.Id, At(3), 1050m)
            ],
            Period,
            Offset);

        Assert.Equal(250m, Assert.Single(result.Mediums).Quantity);
    }

    /// <summary>
    /// 倍率必须参与计算，否则 10kV 计量点的读数会小 40 倍。
    /// </summary>
    [Fact]
    public void Ratio_Should_Be_Applied_To_Difference()
    {
        var meter = Meter(ratio: 40m);

        var result = ParkEnergyAggregator.Aggregate(
            [meter],
            [
                new ParkEnergyReadingInput(meter.Id, At(10), 100m),
                new ParkEnergyReadingInput(meter.Id, At(20), 110m)
            ],
            Period,
            Offset);

        Assert.Equal(400m, Assert.Single(result.Mediums).Quantity);
    }

    /// <summary>
    /// 非累计表（流量计）的读数本身就是区间用量，直接相加，不做差分。
    /// </summary>
    [Fact]
    public void Instantaneous_Meter_Should_Sum_Readings()
    {
        var meter = Meter("WATER", 0.2571m, cumulative: false, name: "1# 水表");

        var result = ParkEnergyAggregator.Aggregate(
            [meter],
            [
                new ParkEnergyReadingInput(meter.Id, At(2), 12m),
                new ParkEnergyReadingInput(meter.Id, At(9), 8m)
            ],
            Period,
            Offset);

        Assert.Equal(20m, Assert.Single(result.Mediums).Quantity);
    }

    /// <summary>
    /// 综合能耗只汇总一次能源。压缩空气由电转换而来，一并折标会把它算两遍。
    /// </summary>
    [Fact]
    public void Total_Should_Only_Include_Primary_Energy()
    {
        var electricity = Meter("ELEC", 0.1229m, name: "总表");
        var air = Meter("AIR", null, isPrimary: false, name: "空压表");

        var readings = new[]
        {
            new ParkEnergyReadingInput(electricity.Id, At(1), 0m),
            new ParkEnergyReadingInput(electricity.Id, At(20), 1000m),
            new ParkEnergyReadingInput(air.Id, At(1), 0m),
            new ParkEnergyReadingInput(air.Id, At(20), 500m)
        };

        var result = ParkEnergyAggregator.Aggregate([electricity, air], readings, Period, Offset);

        Assert.Equal(2, result.Mediums.Count);
        Assert.Equal(0.1229m, result.TotalTce);
        Assert.Contains(result.Mediums, usage => usage.Code == "AIR" && !usage.IsPrimaryEnergy);
    }

    /// <summary>
    /// 蒸汽没有折标系数时，实物量照常出现，但不进综合能耗，并且要给出提示。
    /// 给蒸汽编一个默认系数会让报表显得精确，实际是错的。
    /// </summary>
    [Fact]
    public void Primary_Medium_Without_Factor_Should_Warn()
    {
        var steam = Meter("STEAM", null, name: "蒸汽总表");

        var result = ParkEnergyAggregator.Aggregate(
            [steam],
            [
                new ParkEnergyReadingInput(steam.Id, At(1), 0m),
                new ParkEnergyReadingInput(steam.Id, At(20), 120m)
            ],
            Period,
            Offset);

        var usage = Assert.Single(result.Mediums);
        Assert.Equal(120m, usage.Quantity);
        Assert.Null(usage.CoalEquivalentTce);
        Assert.Equal(0m, result.TotalTce);
        Assert.Contains(result.Warnings, warning => warning.Contains("折标煤系数"));
    }

    /// <summary>
    /// 示数回退（换表、清零）按 0 计并提示。取绝对值会凭空造出一笔巨额能耗。
    /// </summary>
    [Fact]
    public void Meter_Reset_Should_Count_As_Zero()
    {
        var meter = Meter();

        var result = ParkEnergyAggregator.Aggregate(
            [meter],
            [
                new ParkEnergyReadingInput(meter.Id, At(5), 5000m),
                new ParkEnergyReadingInput(meter.Id, At(15), 20m)
            ],
            Period,
            Offset);

        Assert.Equal(0m, Assert.Single(result.Mediums).Quantity);
        Assert.Contains(result.Warnings, warning => warning.Contains("示数回退"));
    }

    /// <summary>
    /// 组织上卷：按折标量降序，便于页面直接排名。
    /// </summary>
    [Fact]
    public void Orgs_Should_Be_Aggregated_And_Sorted()
    {
        var big = Meter(name: "大表");
        var small = Meter(name: "小表");

        var smallOrg = new ParkEnergyMeterInput(
            small.Id, small.Code, small.Name, "ELEC", "电", "kWh", true, 0.1229m, true, 1m,
            Guid.Parse("22222222-2222-2222-2222-222222222222"), "印钞车间");

        var readings = new[]
        {
            new ParkEnergyReadingInput(big.Id, At(1), 0m),
            new ParkEnergyReadingInput(big.Id, At(20), 1000m),
            new ParkEnergyReadingInput(small.Id, At(1), 0m),
            new ParkEnergyReadingInput(small.Id, At(20), 200m)
        };

        var result = ParkEnergyAggregator.Aggregate([big, smallOrg], readings, Period, Offset);

        Assert.Equal(2, result.Orgs.Count);
        Assert.Equal("钞纸车间", result.Orgs[0].OrgUnitName);
        Assert.Equal("印钞车间", result.Orgs[1].OrgUnitName);
    }

    /// <summary>
    /// 期间内没有任何读数时，状态必须是「没有读数」，页面据此走空态而不是画一张全 0 的图。
    /// </summary>
    [Fact]
    public void No_Readings_Should_Report_Empty()
    {
        var result = ParkEnergyAggregator.Aggregate([Meter()], [], Period, Offset);

        Assert.False(result.HasReadings);
        Assert.Null(result.DataThrough);
        Assert.Empty(result.Mediums);
    }

    /// <summary>
    /// 小时负荷：两小时内走了 100 kWh，平均功率就是 50 kW。
    /// 直接拿电量当功率画，纵轴会变成电量的量级，与「kW」标签对不上。
    /// </summary>
    [Fact]
    public void Hourly_Load_Should_Convert_Energy_To_Average_Power()
    {
        var meter = Meter();
        var day = new DateOnly(2026, 11, 10);

        var readings = new[]
        {
            new ParkEnergyReadingInput(meter.Id, new DateTimeOffset(2026, 11, 10, 8, 0, 0, Offset), 0m),
            new ParkEnergyReadingInput(meter.Id, new DateTimeOffset(2026, 11, 10, 10, 0, 0, Offset), 100m)
        };

        var load = ParkEnergyAggregator.BuildHourlyLoad([meter], readings, day, Offset);

        Assert.Equal(50m, load[8]);
        Assert.Equal(50m, load[9]);
        Assert.Null(load[0]);
        Assert.Equal(24, load.Length);
    }

    /// <summary>
    /// 跨多个整点时必须平均分摊。曾经把「剩余量 × 本片时长占比」当作每片用量，
    /// 4 小时段被分成 4000/3000/2250/6750 这样几何衰减的锯齿，页面上的曲线
    /// 会莫名其妙地起伏，而每一段的总量又是对的。
    /// </summary>
    [Fact]
    public void Hourly_Load_Should_Split_Evenly_Across_Multiple_Hours()
    {
        var meter = Meter();
        var day = new DateOnly(2026, 11, 10);

        var readings = new[]
        {
            new ParkEnergyReadingInput(meter.Id, new DateTimeOffset(2026, 11, 10, 8, 0, 0, Offset), 0m),
            new ParkEnergyReadingInput(meter.Id, new DateTimeOffset(2026, 11, 10, 12, 0, 0, Offset), 400m)
        };

        var load = ParkEnergyAggregator.BuildHourlyLoad([meter], readings, day, Offset);

        foreach (var hour in new[] { 8, 9, 10, 11 })
        {
            Assert.Equal(100m, load[hour]);
        }
    }

    /// <summary>
    /// 负荷曲线只看电表。把水表算进去，曲线会在用水高峰出现一个不属于电的尖峰。
    /// </summary>
    [Fact]
    public void Hourly_Load_Should_Ignore_Non_Electric_Meters()
    {
        var water = Meter("WATER", 0.2571m, cumulative: false, name: "水表");
        var day = new DateOnly(2026, 11, 10);

        var readings = new[]
        {
            new ParkEnergyReadingInput(water.Id, new DateTimeOffset(2026, 11, 10, 9, 0, 0, Offset), 60m)
        };

        var load = ParkEnergyAggregator.BuildHourlyLoad([water], readings, day, Offset);

        Assert.All(load, value => Assert.Null(value));
    }
}
