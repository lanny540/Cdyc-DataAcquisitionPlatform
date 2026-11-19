namespace DAP.Core.Shared.Catalog;

/// <summary>
/// 能源板块的内置字典。
///
/// 放在共享目录而不是初始化器内部，是为了让页面下拉框、报表口径与种子数据
/// 引用同一份定义——三处各写一遍，早晚会出现某个下拉框少了「公共设施」这种偏差。
/// 同时也让守卫测试能直接断言分类体系本身。
/// </summary>
public static class EnergyDictionaryCatalog
{
    /// <summary>
    /// 内置能源介质。
    ///
    /// 折标煤系数取 GB/T 2589 的当量值。蒸汽系数取决于焓值、压缩空气与制冷量
    /// 没有固定系数，三者留空：给一个看起来合理的默认值，报表会显得精确，但那是错的。
    /// </summary>
    public static IReadOnlyList<EnergyMediumSeed> Media { get; } =
    [
        new("ELEC", "电", "kWh", true, 0.1229m, 10),
        new("WATER", "水", "t", true, 0.2571m, 20),
        new("STEAM", "蒸汽", "t", true, null, 30),
        new("COOLING", "空调制冷量", "GJ", false, null, 40),
        new("AIR", "压缩空气", "m³", false, null, 50)
    ];

    /// <summary>
    /// 内置用能用途。
    ///
    /// 五类对应五个责任主体：生产（车间）、辅助生产（动力车间）、管理（行政）、
    /// 安防（保卫）、公共设施（后勤）。少归并一类，就有一个责任方没有账可查。
    /// </summary>
    public static IReadOnlyList<EnergyCategorySeed> UsageTypes { get; } =
    [
        new("PROD", "生产", 10),
        new("AUX", "辅助生产", 20),
        new("OFFICE", "管理", 30),
        new("SECURITY", "安防", 40),
        new("PUBLIC", "公共设施", 50)
    ];

    /// <summary>
    /// 内置设备系统。
    ///
    /// 照明在这里而不是在用途里：车间照明与办公照明都是照明，
    /// 但用途跟着所在区域走，系统跟着设备类型走。
    /// </summary>
    public static IReadOnlyList<EnergyCategorySeed> Systems { get; } =
    [
        new("PROCESS", "工艺设备", 10),
        new("LIGHTING", "照明", 20),
        new("HVAC", "空调通风", 30),
        new("POWER", "动力站房", 40),
        new("IT", "IT 与弱电", 50)
    ];
}

/// <summary>
/// 能源介质的种子定义。
/// </summary>
/// <param name="Code">介质编码。</param>
/// <param name="Name">介质名称。</param>
/// <param name="Unit">计量单位。</param>
/// <param name="IsPrimaryEnergy">是否参与综合能耗求和。</param>
/// <param name="CoalFactor">折标煤系数，未知时为空。</param>
/// <param name="SortOrder">显示顺序。</param>
public sealed record EnergyMediumSeed(
    string Code,
    string Name,
    string Unit,
    bool IsPrimaryEnergy,
    decimal? CoalFactor,
    int SortOrder);

/// <summary>
/// 能源分类项（用途、设备系统）的种子定义。
/// </summary>
/// <param name="Code">分类编码。</param>
/// <param name="Name">分类名称。</param>
/// <param name="SortOrder">显示顺序。</param>
public sealed record EnergyCategorySeed(string Code, string Name, int SortOrder);