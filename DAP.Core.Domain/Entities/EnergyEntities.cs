namespace DAP.Core.Domain.Entities;

/// <summary>
/// 表示一种能源介质，例如电、水、蒸汽。
///
/// **只做一套分类**：不引入「分类方案」这一层。多套方案并存时，
/// 同一块表要在每套方案下各打一次标签，几百块表就会被打上千次，
/// 必然出现漏标与自相矛盾；而三分类能看出的东西，五分类筛选一下同样能看到。
/// 将来真需要第二套视角（例如财务的成本口径）再加方案表，届时是纯增量改动。
/// </summary>
public sealed class EnergyMedium
{
    /// <summary>
    /// 获取或设置介质标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置介质编码，例如 ELEC、STEAM。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置介质名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置计量单位，例如 kWh、t、GJ。
    /// </summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置是否为一次能源。
    ///
    /// 只有一次能源参与「综合能耗」求和。压缩空气与空调制冷量的能量
    /// 本来就来自电或蒸汽，一并折标会把这部分算两遍，总量直接虚高；
    /// 它们作为分项展示，口径是折算电耗。外购冷、外购压缩空气时才改判为一次能源。
    /// </summary>
    public bool IsPrimaryEnergy { get; set; }

    /// <summary>
    /// 获取或设置折标煤系数（kgce/单位）。
    ///
    /// 允许为空：蒸汽的系数取决于焓值，压缩空气与冷量没有固定值，
    /// 强行给一个默认数会让报表看起来精确、实际是错的。
    /// </summary>
    public decimal? CoalFactor { get; set; }

    /// <summary>
    /// 获取或设置显示顺序。
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 获取或设置是否启用。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 获取或设置更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// 获取或设置使用该介质的计量点集合。
    /// </summary>
    public ICollection<EnergyMeter> Meters { get; set; } = [];
}

/// <summary>
/// 表示用能用途，回答「这笔能源为谁服务」。
///
/// 五类对应五个责任主体：生产（车间）、辅助生产（动力车间）、
/// 管理（行政）、安防（保卫）、公共设施（后勤）。少归并一类，
/// 就有一个责任方没有账可查——办公楼并进公共设施后，
/// 「办公楼节能」这个议题在报表上会直接消失。
/// </summary>
public sealed class EnergyUsageType
{
    /// <summary>
    /// 获取或设置用途标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置用途编码，例如 PROD、AUX、OFFICE、SECURITY、PUBLIC。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置用途名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置显示顺序。
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 获取或设置是否为内置项。内置项不允许删除，只允许停用——
    /// 已有计量点引用它时删掉，历史报表的口径就断了。
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// 获取或设置是否启用。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 获取或设置更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// 获取或设置使用该用途的计量点集合。
    /// </summary>
    public ICollection<EnergyMeter> Meters { get; set; } = [];
}

/// <summary>
/// 表示设备系统，回答「什么设备在用电」。
///
/// 照明放在这里而不是用途里：车间照明与办公照明都是照明，
/// 但用途完全不同。用途跟着所在区域走，系统跟着设备类型走。
/// </summary>
public sealed class EnergySystemType
{
    /// <summary>
    /// 获取或设置系统标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置系统编码，例如 PROCESS、LIGHTING、HVAC、POWER、IT。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置系统名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置显示顺序。
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 获取或设置是否为内置项。
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// 获取或设置是否启用。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 获取或设置更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// 获取或设置使用该系统的计量点集合。
    /// </summary>
    public ICollection<EnergyMeter> Meters { get; set; } = [];
}

/// <summary>
/// 表示能源组织节点，构成「园区 → 部门（车间）→ 工序（工段）」三级树。
///
/// 不复用 <see cref="Department"/> 作主键：工序不是组织单元，
/// 塞进部门表会污染认证体系里「班组 ≠ 组织单元」的既有约定。
/// 部门级节点额外挂一个可空的 <see cref="DepartmentId"/>，
/// 用于把能耗账和权限体系对齐，而不是让两套树各自生长。
/// </summary>
public sealed class EnergyOrgUnit
{
    /// <summary>
    /// 园区级节点。
    /// </summary>
    public const string ParkLevel = "park";

    /// <summary>
    /// 部门（车间）级节点。
    /// </summary>
    public const string DepartmentLevel = "department";

    /// <summary>
    /// 工序（工段）级节点。
    /// </summary>
    public const string ProcessLevel = "process";

    /// <summary>
    /// 获取或设置节点标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置节点编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置节点名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置节点层级，取值为 park、department 或 process。
    /// </summary>
    public string Level { get; set; } = DepartmentLevel;

    /// <summary>
    /// 获取或设置上级节点标识。园区节点为空。
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// 获取或设置关联的组织部门标识。仅部门级节点使用，用于与权限体系对齐。
    /// </summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>
    /// 获取或设置显示顺序。
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 获取或设置是否启用。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 获取或设置更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// 获取或设置上级节点。
    /// </summary>
    public EnergyOrgUnit? Parent { get; set; }

    /// <summary>
    /// 获取或设置下级节点集合。
    /// </summary>
    public ICollection<EnergyOrgUnit> Children { get; set; } = [];

    /// <summary>
    /// 获取或设置挂在当前节点上的计量点集合。
    /// </summary>
    public ICollection<EnergyMeter> Meters { get; set; } = [];
}

/// <summary>
/// 表示能源计量点，是能耗数据的最小归属单元。
///
/// 一块表同时挂四个维度：介质、组织、用途、系统。这是「同一份数据、
/// 多个看法」的实现方式——按用途看构成、按组织看排名、按系统看分项，
/// 都不需要重复采集。
/// </summary>
public sealed class EnergyMeter
{
    /// <summary>
    /// 获取或设置计量点标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置计量点编码，例如电表编号。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置计量点名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置介质标识。
    /// </summary>
    public Guid MediumId { get; set; }

    /// <summary>
    /// 获取或设置所属组织节点标识。
    /// </summary>
    public Guid OrgUnitId { get; set; }

    /// <summary>
    /// 获取或设置用能用途标识。
    /// </summary>
    public Guid UsageTypeId { get; set; }

    /// <summary>
    /// 获取或设置设备系统标识。
    /// </summary>
    public Guid SystemTypeId { get; set; }

    /// <summary>
    /// 获取或设置上级计量点标识，用于构成总分表关系。
    ///
    /// 没有这层关系就永远算不出总分表差额，而园区能源最难查的
    /// 恰恰是线损与管损——差额必须能单列为「损耗」，而不是摊给某个部门。
    /// </summary>
    public Guid? ParentMeterId { get; set; }

    /// <summary>
    /// 获取或设置是否为总表。
    /// </summary>
    public bool IsSummary { get; set; }

    /// <summary>
    /// 获取或设置倍率。读数乘以倍率才是实际用量。
    /// </summary>
    public decimal Ratio { get; set; } = 1m;

    /// <summary>
    /// 获取或设置读数类型是否为累计值。
    ///
    /// 电表通常给的是示数（累计），用量要差分；流量计给的是瞬时值。
    /// 两种读数混在一起算，会得出荒谬的日用量。
    /// </summary>
    public bool IsCumulative { get; set; } = true;

    /// <summary>
    /// 获取或设置是否启用。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置备注。
    /// </summary>
    public string? Remark { get; set; }

    /// <summary>
    /// 获取或设置创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 获取或设置更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// 获取或设置介质。
    /// </summary>
    public EnergyMedium? Medium { get; set; }

    /// <summary>
    /// 获取或设置所属组织节点。
    /// </summary>
    public EnergyOrgUnit? OrgUnit { get; set; }

    /// <summary>
    /// 获取或设置用能用途。
    /// </summary>
    public EnergyUsageType? UsageType { get; set; }

    /// <summary>
    /// 获取或设置设备系统。
    /// </summary>
    public EnergySystemType? SystemType { get; set; }

    /// <summary>
    /// 获取或设置上级计量点。
    /// </summary>
    public EnergyMeter? ParentMeter { get; set; }

    /// <summary>
    /// 获取或设置下级计量点集合。
    /// </summary>
    public ICollection<EnergyMeter> ChildMeters { get; set; } = [];

    /// <summary>
    /// 获取或设置读数集合。
    /// </summary>
    public ICollection<EnergyReading> Readings { get; set; } = [];
}

/// <summary>
/// 表示一条能源读数。
/// </summary>
public sealed class EnergyReading
{
    /// <summary>
    /// 手工补录。
    /// </summary>
    public const string ManualSource = "manual";

    /// <summary>
    /// 采集系统写入。
    /// </summary>
    public const string CollectionSource = "collection";

    /// <summary>
    /// 批量导入。
    /// </summary>
    public const string ImportSource = "import";

    /// <summary>
    /// 获取或设置读数标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置计量点标识。
    /// </summary>
    public Guid MeterId { get; set; }

    /// <summary>
    /// 获取或设置抄见时刻。
    /// </summary>
    public DateTimeOffset ReadAt { get; set; }

    /// <summary>
    /// 获取或设置读数原始值（未乘倍率）。
    /// </summary>
    public decimal Value { get; set; }

    /// <summary>
    /// 获取或设置数据来源，取值为 manual、collection 或 import。
    /// </summary>
    public string Source { get; set; } = ManualSource;

    /// <summary>
    /// 获取或设置创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 获取或设置所属计量点。
    /// </summary>
    public EnergyMeter? Meter { get; set; }
}
