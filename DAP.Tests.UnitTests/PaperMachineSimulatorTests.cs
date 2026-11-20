using DAP.Presentation.BlazorWeb.Client.Components;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 钞纸机工艺线模拟器的契约测试。
///
/// 这些断言不是「测试假数据」，而是防止孪生视图在刷新时出现「同一时刻两次读到不同值」
/// 或「数值漂到工艺上不可能的范围」——现场看到这类现象会直接怀疑采集链路。
/// </summary>
public class PaperMachineSimulatorTests
{
    private static readonly DateTimeOffset Sample = new(2026, 11, 14, 16, 30, 0, TimeSpan.FromHours(8));

    /// <summary>
    /// 同一时刻多次采样必须完全一致：刷新页面不该看到数值自己变。
    /// </summary>
    [Fact]
    public void Snapshot_Should_Be_Deterministic()
    {
        PaperMachineSnapshot first = PaperMachineSimulator.Snapshot(Sample);
        PaperMachineSnapshot second = PaperMachineSimulator.Snapshot(Sample);

        Assert.Equal(first.Stations.Count, second.Stations.Count);

        for (int i = 0; i < first.Stations.Count; i++)
        {
            Assert.Equal(first.Stations[i].Code, second.Stations[i].Code);

            for (int m = 0; m < first.Stations[i].Metrics.Count; m++)
            {
                Assert.Equal(first.Stations[i].Metrics[m].DisplayValue, second.Stations[i].Metrics[m].DisplayValue);
            }
        }

        Assert.Equal(
            first.KeyMetrics.Select(metric => metric.DisplayValue),
            second.KeyMetrics.Select(metric => metric.DisplayValue));
    }

    /// <summary>
    /// 换一个采样桶后至少要有一项变化，否则「实时刷新」看起来像卡住了。
    /// </summary>
    [Fact]
    public void Snapshot_Should_Change_Between_Ticks()
    {
        DateTimeOffset next = Sample + PaperMachineSimulator.Tick;

        string[] before = PaperMachineSimulator.Snapshot(Sample).Stations
            .SelectMany(station => station.Metrics)
            .Select(metric => metric.DisplayValue)
            .ToArray();

        string[] after = PaperMachineSimulator.Snapshot(next).Stations
            .SelectMany(station => station.Metrics)
            .Select(metric => metric.DisplayValue)
            .ToArray();

        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// 工艺顺序必须与物料流向一致：投料 → 打浆 → 配浆流送 → 网部 → 压榨 → 干燥 → 压光 → 卷取。
    /// 顺序错了，孪生图画出来的流程就是错的。
    /// </summary>
    [Fact]
    public void Stations_Should_Follow_Process_Order()
    {
        PaperMachineSnapshot snapshot = PaperMachineSimulator.Snapshot(Sample);

        Assert.Equal(
            ["FEED-01", "REF-01", "STK-01", "WET-01", "PRS-01", "DRY-01", "CLD-01", "REW-01"],
            snapshot.Stations.Select(station => station.Code));

        Assert.Equal(3, snapshot.Stations.Count(station => station.Stage == PaperMachineStage.Stock));
        Assert.Equal(2, snapshot.Stations.Count(station => station.Stage == PaperMachineStage.WetEnd));
        Assert.Equal(3, snapshot.Stations.Count(station => station.Stage == PaperMachineStage.DryEnd));
    }

    /// <summary>
    /// 数值必须落在工艺上说得通的区间：定量 76~81 g/m²、车速 405~435 m/min 等。
    /// 越界的数值在孪生图上会表现为「设备异常」，比不显示更糟。
    /// </summary>
    [Theory]
    [InlineData("WET-01", "定量", 76d, 81d)]
    [InlineData("PRS-01", "出压榨干度", 43d, 46.5d)]
    [InlineData("DRY-01", "烘缸温度", 113d, 123d)]
    [InlineData("REW-01", "车速", 405d, 435d)]
    [InlineData("STK-01", "上网浓度", 0.75d, 0.9d)]
    public void Metrics_Should_Stay_In_Plausible_Range(string code, string label, double min, double max)
    {
        // 连续采样 2 小时（每 5 秒一次），确保慢波与噪声的极值都被覆盖。
        for (int step = 0; step < 1440; step++)
        {
            PaperMachineSnapshot snapshot = PaperMachineSimulator.Snapshot(Sample.AddSeconds(step * 5));
            TwinMetric metric = snapshot.Stations
                .Single(station => station.Code == code)
                .Metrics
                .Single(item => item.Label == label);

            Assert.InRange(metric.Value, min, max);
        }
    }

    /// <summary>
    /// 沿程干度必须与工位卡片上的同名参数一致：同屏出现两个不同的「出压榨干度」
    /// 会让人以为其中一处是错数据。两处共用同一份规格与同一个采样桶。
    /// </summary>
    [Fact]
    public void Dewatering_Should_Match_Station_Metrics()
    {
        PaperMachineSnapshot snapshot = PaperMachineSimulator.Snapshot(Sample);

        Assert.Equal(4, snapshot.Dewatering.Count);
        Assert.Equal(
            ["上网浓度", "出网部干度", "出压榨干度", "成纸干度"],
            snapshot.Dewatering.Select(metric => metric.Label));

        Dictionary<string, string> stationValues = snapshot.Stations
            .SelectMany(station => station.Metrics)
            .ToDictionary(metric => metric.Label, metric => metric.DisplayValue);

        foreach (TwinMetric point in snapshot.Dewatering)
        {
            Assert.Equal(stationValues[point.Label], point.DisplayValue);
        }
    }

    /// <summary>
    /// 断纸次数是零幅值的计数器：模拟器不该凭空造出一次断纸。
    /// </summary>
    [Fact]
    public void Shift_Totals_Should_Keep_Break_Count_Zero()
    {
        for (int step = 0; step < 120; step++)
        {
            TwinMetric metric = PaperMachineSimulator.Snapshot(Sample.AddSeconds(step * 5)).ShiftTotals
                .Single(item => item.Label == "断纸");

            Assert.Equal("0", metric.DisplayValue);
        }
    }

    /// <summary>
    /// 小数位按定义输出，且不受运行环境区域设置影响（德语区会把小数点写成逗号）。
    /// </summary>
    [Fact]
    public void Display_Value_Should_Use_Invariant_Decimal_Point()
    {
        TwinMetric metric = new("上网浓度", 0.82, 2, "%");

        Assert.Equal("0.82", metric.DisplayValue);
    }
}
