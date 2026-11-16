using System.Globalization;

namespace DAP.Presentation.BlazorWeb.Client.Components;

/// <summary>
/// 纸机工艺段。三段的分法沿用现场叫法：备浆属于纸机之前的上浆系统，
/// 湿部从网部开始到压榨结束，干部从干燥开始到卷取结束。
/// </summary>
public enum PaperMachineStage
{
    /// <summary>备浆：投料、打浆、配浆流送。</summary>
    Stock,

    /// <summary>湿部：网部成形与压榨脱水。</summary>
    WetEnd,

    /// <summary>干部：干燥、压光与卷取。</summary>
    DryEnd
}

/// <summary>
/// 单个工艺参数。数值与格式分开存：界面只负责展示，取值范围由模拟器保证。
/// </summary>
/// <param name="Label">参数名。</param>
/// <param name="Value">数值。</param>
/// <param name="Decimals">小数位数。</param>
/// <param name="Unit">单位。</param>
public sealed record TwinMetric(string Label, double Value, int Decimals, string Unit)
{
    /// <summary>
    /// 展示文本。用不变文化格式化，避免在德语等区域把小数点显示成逗号。
    /// </summary>
    public string DisplayValue => Value.ToString("F" + Decimals, CultureInfo.InvariantCulture);
}

/// <summary>
/// 一个工位的工况快照。
/// </summary>
/// <param name="Code">设备位号，例如 <c>WET-01</c>。</param>
/// <param name="Name">工位名称。</param>
/// <param name="Role">一句话职责。</param>
/// <param name="Stage">所属工艺段。</param>
/// <param name="Metrics">该工位的工艺参数。</param>
public sealed record TwinStation(
    string Code,
    string Name,
    string Role,
    PaperMachineStage Stage,
    IReadOnlyList<TwinMetric> Metrics)
{
    /// <summary>
    /// 是否运行中。当前模拟线全部运行；引入真实数据后由工况驱动。
    /// </summary>
    public bool IsRunning => true;

    /// <summary>
    /// 状态文案。
    /// </summary>
    public string StatusText => IsRunning ? "运行" : "停机";
}

/// <summary>
/// 一条纸机的整线快照。
/// </summary>
/// <param name="CapturedAt">采样时刻。</param>
/// <param name="LineName">机台名称。</param>
/// <param name="LineCode">机台编号。</param>
/// <param name="Stations">工位工况。</param>
/// <param name="KeyMetrics">顶部关键指标。</param>
/// <param name="ShiftTotals">本班累计。</param>
public sealed record PaperMachineSnapshot(
    DateTimeOffset CapturedAt,
    string LineName,
    string LineCode,
    IReadOnlyList<TwinStation> Stations,
    IReadOnlyList<TwinMetric> KeyMetrics,
    IReadOnlyList<TwinMetric> ShiftTotals,
    IReadOnlyList<TwinMetric> Dewatering);

/// <summary>
/// 钞纸机工艺线的确定性模拟器。
///
/// **为什么不用 <c>Random</c>**：页面每 5 秒采样一次，随机数会让同一个时刻刷新出不同数值，
/// 现场核对时会以为数据在跳。这里用 FNV-1a 把「参数名 + 时间桶」压成一个 [0,1) 的噪声，
/// 再叠加一条慢正弦，得到「每次刷新都有变化、但同一时刻必然相同」的曲线。
///
/// 该模拟器只在孪生视图尚未接入真实工况时使用，替换为接口后整个类可以直接删除。
/// </summary>
public static class PaperMachineSimulator
{
    /// <summary>
    /// 采样节拍。与界面上的刷新提示保持一致。
    /// </summary>
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 慢波周期（以采样桶为单位）。90 桶 × 5 秒 ≈ 7.5 分钟一轮，肉眼能看到趋势但不会来回乱跳。
    /// </summary>
    private const double WavePeriodBuckets = 90d;

    /// <summary>
    /// 共用参数：既出现在工位卡片里，也出现在「沿程干度」带上。
    /// 只定义一份，保证两处读到的是同一个采样值——同屏出现两个互相矛盾的干度会让人怀疑数据。
    /// </summary>
    private static readonly MetricSpec InletConsistency = new("上网浓度", 0.82, 0.03, 2, "%");

    private static readonly MetricSpec WireDryness = new("出网部干度", 21.8, 0.6, 1, "%");

    private static readonly MetricSpec PressDryness = new("出压榨干度", 44.6, 0.8, 1, "%");

    private static readonly MetricSpec FinalDryness = new("成纸干度", 94.2, 0.35, 1, "%");

    private static readonly StationSpec[] StationSpecs =
    [
        new("FEED-01", "投料", "浆板与损纸投入", PaperMachineStage.Stock,
        [
            new MetricSpec("投料量", 4.8, 0.25, 1, "t/h"),
            new MetricSpec("浆板水分", 9.6, 0.4, 1, "%"),
            new MetricSpec("碎浆浓度", 4.2, 0.15, 1, "%")
        ]),
        new("REF-01", "打浆", "双盘磨叩解", PaperMachineStage.Stock,
        [
            new MetricSpec("叩解度", 42, 1.2, 0, "°SR"),
            new MetricSpec("打浆功率", 186, 9, 0, "kW"),
            new MetricSpec("通过量", 4.6, 0.2, 1, "t/h")
        ]),
        new("STK-01", "配浆流送", "配浆与除砂", PaperMachineStage.Stock,
        [
            new MetricSpec("送浆流量", 1260, 45, 0, "m³/h"),
            InletConsistency,
            new MetricSpec("除砂压差", 0.18, 0.01, 2, "MPa")
        ]),
        new("WET-01", "网部", "成形与脱水", PaperMachineStage.WetEnd,
        [
            new MetricSpec("定量", 78.4, 0.9, 1, "g/m²"),
            new MetricSpec("网部真空", -42, 1.5, 0, "kPa"),
            WireDryness
        ]),
        new("PRS-01", "压榨", "三道压区脱水", PaperMachineStage.WetEnd,
        [
            new MetricSpec("压榨线压", 78, 2.5, 1, "kN/m"),
            PressDryness,
            new MetricSpec("毛毯真空", -58, 2, 0, "kPa")
        ]),
        new("DRY-01", "干燥", "烘缸组干燥", PaperMachineStage.DryEnd,
        [
            new MetricSpec("烘缸汽压", 0.42, 0.02, 2, "MPa"),
            new MetricSpec("烘缸温度", 118, 3, 0, "°C"),
            FinalDryness
        ]),
        new("CLD-01", "压光", "软压光整饰", PaperMachineStage.DryEnd,
        [
            new MetricSpec("压光线压", 52, 2, 1, "kN/m"),
            new MetricSpec("平滑度", 68, 3, 0, "s"),
            new MetricSpec("成纸厚度", 96, 1.5, 0, "µm")
        ]),
        new("REW-01", "卷曲", "卷取与换卷", PaperMachineStage.DryEnd,
        [
            new MetricSpec("车速", 420, 8, 0, "m/min"),
            new MetricSpec("卷径", 1240, 60, 0, "mm"),
            new MetricSpec("卷取张力", 320, 12, 0, "N")
        ])
    ];

    private static readonly MetricSpec[] KeyMetricSpecs =
    [
        new MetricSpec("车速", 420, 8, 0, "m/min"),
        new MetricSpec("定量", 78.4, 0.9, 1, "g/m²"),
        new MetricSpec("成纸水分", 5.8, 0.25, 1, "%"),
        new MetricSpec("运行率", 97.2, 0.4, 1, "%")
    ];

    private static readonly MetricSpec[] ShiftTotalSpecs =
    [
        new MetricSpec("产量", 62.4, 1.2, 1, "t"),
        new MetricSpec("断纸", 0, 0, 0, "次"),
        new MetricSpec("运行时长", 6.8, 0.05, 1, "h"),
        new MetricSpec("吨纸汽耗", 1.82, 0.03, 2, "t/t")
    ];

    /// <summary>
    /// 机台名称。
    /// </summary>
    public const string LineName = "1# 钞纸机";

    /// <summary>
    /// 机台编号。
    /// </summary>
    public const string LineCode = "PM-01";

    /// <summary>
    /// 按给定时刻生成整线快照。同一时刻多次调用结果完全一致。
    /// </summary>
    /// <param name="capturedAt">采样时刻。</param>
    /// <returns>整线快照。</returns>
    public static PaperMachineSnapshot Snapshot(DateTimeOffset capturedAt)
    {
        var bucket = capturedAt.ToUnixTimeSeconds() / (long) Tick.TotalSeconds;

        var stations = StationSpecs
            .Select(spec => new TwinStation(
                spec.Code,
                spec.Name,
                spec.Role,
                spec.Stage,
                spec.Metrics.Select(metric => Measure(metric, bucket)).ToArray()))
            .ToArray();

        return new PaperMachineSnapshot(
            capturedAt,
            LineName,
            LineCode,
            stations,
            KeyMetricSpecs.Select(metric => Measure(metric, bucket)).ToArray(),
            ShiftTotalSpecs.Select(metric => Measure(metric, bucket)).ToArray(),
            DewateringSpecs.Select(metric => Measure(metric, bucket)).ToArray());
    }

    /// <summary>
    /// 沿程干度：上网浓度 → 出网部 → 出压榨 → 成纸，纸机脱水的四个关键节点。
    /// </summary>
    private static readonly MetricSpec[] DewateringSpecs =
    [
        InletConsistency,
        WireDryness,
        PressDryness,
        FinalDryness
    ];

    /// <summary>
    /// 计算一个参数的当前值：慢波负责「像在生产」，哈希噪声负责「每次刷新都不一样」。
    /// </summary>
    private static TwinMetric Measure(MetricSpec spec, long bucket)
    {
        var key = spec.Label + "|" + spec.Unit;
        var phase = Noise01(key + "|phase", 0) * Math.Tau;
        var wave = Math.Sin(Math.Tau * bucket / WavePeriodBuckets + phase);
        var noise = Noise01(key, bucket) * 2 - 1;
        var value = spec.Base + spec.Amplitude * (0.65 * wave + 0.35 * noise);

        return new TwinMetric(spec.Label, Math.Round(value, spec.Decimals), spec.Decimals, spec.Unit);
    }

    /// <summary>
    /// FNV-1a 32 位散列映射到 [0,1)。刻意不用 <c>string.GetHashCode()</c>：
    /// 它在不同进程间不稳定，重启后曲线会整体换一条。
    /// </summary>
    private static double Noise01(string key, long bucket)
    {
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;

            var hash = offset;

            foreach (var ch in key)
            {
                hash ^= ch;
                hash *= prime;
            }

            hash ^= (uint) (bucket & 0xFFFFFFFF);
            hash *= prime;
            hash ^= (uint) ((bucket >> 32) & 0xFFFFFFFF);
            hash *= prime;
            hash ^= hash >> 15;
            hash *= prime;

            return hash / (double) uint.MaxValue;
        }
    }

    /// <summary>
    /// 工位定义。
    /// </summary>
    private sealed record StationSpec(
        string Code,
        string Name,
        string Role,
        PaperMachineStage Stage,
        MetricSpec[] Metrics);

    /// <summary>
    /// 参数定义：基准值、波动幅度、小数位与单位。
    /// </summary>
    private sealed record MetricSpec(string Label, double Base, double Amplitude, int Decimals, string Unit);
}
