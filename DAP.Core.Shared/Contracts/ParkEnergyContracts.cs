namespace DAP.Core.Shared.Contracts;

/// <summary>
/// 园区能源总览的数据状态。
///
/// 三态而非「有数据 / 无数据」两态：示例数据必须能与实测数据区分开。
/// 一旦页面把示例值渲染成和实测一样的样式，截图就会流出去变成汇报材料里的「本月能耗」。
/// </summary>
public static class ParkEnergyDataStatus
{
    /// <summary>实测数据。</summary>
    public const string Live = "live";

    /// <summary>尚无读数。</summary>
    public const string Empty = "empty";

    /// <summary>示例数据，仅供版式预览。</summary>
    public const string Sample = "sample";
}

/// <summary>
/// 表示园区能源总览的一次快照。
///
/// 与钞纸班组能耗的区别：这里是「介质 × 组织 × 时间」的经营视角，
/// 回答的是「全园区用了多少、结构如何、谁占大头」，
/// 而不是某个班组某个班次用了多少。
/// </summary>
/// <param name="Period">统计期间，取值为 month 或 year。</param>
/// <param name="PeriodLabel">统计期间的展示文案。</param>
/// <param name="PeriodStart">统计期间起始日（含）。</param>
/// <param name="PeriodEnd">统计期间结束日（含）。</param>
/// <param name="GeneratedAt">本次快照的生成时刻。</param>
/// <param name="DataThrough">已落库的最新读数时刻；无读数时为空。</param>
/// <param name="DataStatus">数据状态，取值见 <see cref="ParkEnergyDataStatus"/>。</param>
/// <param name="Warnings">口径提示，例如「蒸汽未配置折标系数，未计入综合能耗」。</param>
/// <param name="Kpis">顶部指标卡。无数据时为空集合。</param>
/// <param name="LoadPoints">当日 24 小时负荷。无数据时为空集合。</param>
/// <param name="DeclaredDemandKw">申报需量；未配置时为空。</param>
/// <param name="PrimaryShares">一次能源构成，计入综合能耗。</param>
/// <param name="SecondaryShares">二次能源分项，不计入综合能耗。</param>
/// <param name="OrgRows">各部门能耗占比，按折标量从高到低。</param>
public sealed record ParkEnergyOverviewDto(
    string Period,
    string PeriodLabel,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? DataThrough,
    string DataStatus,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ParkEnergyKpiDto> Kpis,
    IReadOnlyList<ParkEnergyLoadPointDto> LoadPoints,
    decimal? DeclaredDemandKw,
    IReadOnlyList<ParkEnergyShareDto> PrimaryShares,
    IReadOnlyList<ParkEnergyShareDto> SecondaryShares,
    IReadOnlyList<ParkEnergyOrgShareDto> OrgRows);

/// <summary>
/// 顶部指标卡。
/// </summary>
/// <param name="Key">指标键，例如 totalTce、electricity。</param>
/// <param name="Label">指标名称。</param>
/// <param name="Value">数值文本，已按千分位格式化。</param>
/// <param name="Unit">计量单位。</param>
/// <param name="Delta">与上一周期的对比文案；无可比基准时为空。</param>
/// <param name="IsImprovement">
/// 对比是否为改善方向。能耗下降才是改善，
/// 缺这个标记时页面只能按「负数=好」猜，涨的时候就会把提醒画成绿色。
/// </param>
/// <param name="Hint">口径说明。</param>
public sealed record ParkEnergyKpiDto(
    string Key,
    string Label,
    string Value,
    string Unit,
    string? Delta,
    bool? IsImprovement,
    string Hint);

/// <summary>
/// 一个小时级负荷点。
/// </summary>
/// <param name="Hour">小时，0 到 23。</param>
/// <param name="LoadKw">该小时的平均负荷，单位 kW。</param>
public sealed record ParkEnergyLoadPointDto(int Hour, decimal LoadKw);

/// <summary>
/// 构成中的一项。
/// </summary>
/// <param name="Name">名称。</param>
/// <param name="DisplayValue">展示用数值，含单位。</param>
/// <param name="Share">
/// 占比，0 到 1。无法计算时为空——例如二次能源缺少折算系数时，
/// 填 0 会让读者以为这类能源不耗电。
/// </param>
/// <param name="ShareText">占比文本。</param>
public sealed record ParkEnergyShareDto(string Name, string DisplayValue, decimal? Share, string ShareText);

/// <summary>
/// 部门能耗行。
/// </summary>
/// <param name="Name">部门名称。</param>
/// <param name="DisplayValue">展示用能耗值。</param>
/// <param name="Share">占园区比重，0 到 1。</param>
/// <param name="DeltaPercent">与上一周期相比的变化百分比；无可比基准时为空。</param>
public sealed record ParkEnergyOrgShareDto(string Name, string DisplayValue, decimal Share, decimal? DeltaPercent);

/// <summary>
/// 表示一个能源计量点。
///
/// 一块表同时挂介质、组织、用途、系统四个维度：同一份读数可以按用途看构成、
/// 按组织看排名、按系统看分项，不需要重复采集。
/// </summary>
/// <param name="Id">计量点标识。</param>
/// <param name="Code">计量点编码。</param>
/// <param name="Name">计量点名称。</param>
/// <param name="MediumId">介质标识。</param>
/// <param name="MediumCode">介质编码。</param>
/// <param name="MediumName">介质名称。</param>
/// <param name="Unit">计量单位。</param>
/// <param name="OrgUnitId">所属组织节点标识。</param>
/// <param name="OrgUnitName">所属组织节点名称。</param>
/// <param name="UsageTypeId">用途标识。</param>
/// <param name="UsageTypeName">用途名称。</param>
/// <param name="SystemTypeId">设备系统标识。</param>
/// <param name="SystemTypeName">设备系统名称。</param>
/// <param name="IsCumulative">读数是否为累计示数。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="Ratio">倍率。读数乘倍率才是实际用量。</param>
/// <param name="ParentMeterCode">上级计量点编码，用于构成总分表关系。</param>
/// <param name="Remark">备注。</param>
public sealed record EnergyMeterDto(
    Guid Id,
    string Code,
    string Name,
    Guid MediumId,
    string MediumCode,
    string MediumName,
    string Unit,
    Guid OrgUnitId,
    string OrgUnitName,
    Guid UsageTypeId,
    string UsageTypeName,
    Guid SystemTypeId,
    string SystemTypeName,
    bool IsCumulative,
    bool IsEnabled,
    decimal Ratio,
    string? ParentMeterCode,
    string? Remark);

/// <summary>
/// 表示能源计量点的新建或更新请求。
///
/// 四个维度都是必填：允许留空的话，这块表的读数在按用途、按系统的报表里
/// 会直接消失，而且不会报错——「有些电不知道去哪了」是这类系统最常见的问题。
/// </summary>
/// <param name="Id">计量点标识，为空表示新建。</param>
/// <param name="Code">计量点编码，全园区唯一。</param>
/// <param name="Name">计量点名称。</param>
/// <param name="MediumId">介质标识。</param>
/// <param name="OrgUnitId">所属组织节点标识。</param>
/// <param name="UsageTypeId">用途标识。</param>
/// <param name="SystemTypeId">设备系统标识。</param>
/// <param name="ParentMeterId">上级计量点标识，用于总分表关系。</param>
/// <param name="IsCumulative">读数是否为累计示数。</param>
/// <param name="Ratio">倍率。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="Remark">备注。</param>
public sealed record EnergyMeterUpsertRequest(
    Guid? Id,
    string Code,
    string Name,
    Guid MediumId,
    Guid OrgUnitId,
    Guid UsageTypeId,
    Guid SystemTypeId,
    Guid? ParentMeterId,
    bool IsCumulative,
    decimal Ratio,
    bool IsEnabled,
    string? Remark);

/// <summary>
/// 表示一次批量读数写入请求。
///
/// 正式环境的能源管理平台按这个结构推送。开发环境没有数据源，
/// 因此端点要能在无数据时正常返回 0 条，而不是报错。
/// </summary>
/// <param name="Readings">读数条目。</param>
public sealed record IngestEnergyReadingsRequest(IReadOnlyCollection<IngestEnergyReadingItem> Readings);

/// <summary>
/// 表示一条待写入的读数。
/// </summary>
/// <param name="MeterCode">计量点编码，不是标识——外部系统只可能知道编码。</param>
/// <param name="ReadAt">抄见时刻。</param>
/// <param name="Value">
/// 读数。累计表（电表）填示数，系统自行差分；
/// 非累计表（流量计）填**本区间用量**，不是瞬时速率。
/// 这条约定必须由接入方遵守，否则会差出两个数量级。
/// </param>
/// <param name="Source">数据来源，取值为 manual、collection 或 import。</param>
public sealed record IngestEnergyReadingItem(string MeterCode, DateTimeOffset ReadAt, decimal Value, string? Source);

/// <summary>
/// 表示批量读数写入的结果。
/// </summary>
/// <param name="Accepted">新写入的条数。</param>
/// <param name="Updated">覆盖已有抄见时刻的条数。</param>
/// <param name="Rejected">因编码未知或数值非法被拒绝的条数。</param>
/// <param name="Messages">拒绝原因，最多保留前 20 条。</param>
public sealed record IngestEnergyReadingsResultDto(
    int Accepted,
    int Updated,
    int Rejected,
    IReadOnlyCollection<string> Messages);

/// <summary>
/// 表示能源字典的完整清单，供采集点打标时选择。
/// </summary>
/// <param name="Media">介质。</param>
/// <param name="UsageTypes">用途。</param>
/// <param name="Systems">设备系统。</param>
/// <param name="OrgUnits">组织节点，已按层级排序。</param>
public sealed record EnergyDictionaryDto(
    IReadOnlyList<EnergyMediumDto> Media,
    IReadOnlyList<EnergyDictionaryItemDto> UsageTypes,
    IReadOnlyList<EnergyDictionaryItemDto> Systems,
    IReadOnlyList<EnergyOrgUnitDto> OrgUnits);

/// <summary>
/// 能源介质。单独一个类型而不是复用字典项：介质要带单位、是否一次能源与折标系数，
/// 这三项决定了它能不能进综合能耗，塞进通用字典项只能拼在说明文字里。
/// </summary>
/// <param name="Id">标识。</param>
/// <param name="Code">编码。</param>
/// <param name="Name">名称。</param>
/// <param name="Unit">计量单位。</param>
/// <param name="IsPrimaryEnergy">是否为一次能源，只有一次能源计入综合能耗。</param>
/// <param name="CoalFactor">折标煤系数，单位 kgce/计量单位；未配置时为空。</param>
public sealed record EnergyMediumDto(
    Guid Id,
    string Code,
    string Name,
    string Unit,
    bool IsPrimaryEnergy,
    decimal? CoalFactor);

/// <summary>
/// 字典项。
/// </summary>
/// <param name="Id">标识。</param>
/// <param name="Code">编码。</param>
/// <param name="Name">名称。</param>
/// <param name="Extra">
/// 附加说明。介质用来带单位与折标系数，
/// 界面上要能看出「蒸汽没有折标系数」而不是以为漏填了。
/// </param>
public sealed record EnergyDictionaryItemDto(Guid Id, string Code, string Name, string? Extra);

/// <summary>
/// 能源组织节点。
/// </summary>
/// <param name="Id">标识。</param>
/// <param name="Code">编码。</param>
/// <param name="Name">名称。</param>
/// <param name="Level">层级，取值为 park、department 或 process。</param>
/// <param name="ParentId">上级节点标识。</param>
/// <param name="Depth">从园区算起的层级深度，园区为 0。</param>
public sealed record EnergyOrgUnitDto(Guid Id, string Code, string Name, string Level, Guid? ParentId, int Depth);

/// <summary>
/// 表示能源组织节点的新建或更新请求。
/// </summary>
/// <param name="Id">节点标识，为空表示新建。</param>
/// <param name="Code">节点编码。</param>
/// <param name="Name">节点名称。</param>
/// <param name="Level">层级，取值为 park、department 或 process。</param>
/// <param name="ParentId">上级节点标识。园区级必须为空，其余必须非空。</param>
public sealed record EnergyOrgUnitUpsertRequest(
    Guid? Id,
    string Code,
    string Name,
    string Level,
    Guid? ParentId);

/// <summary>
/// 表示修改介质折标煤系数的请求。
///
/// 只允许改系数，不允许改编码、名称与单位：这三项一旦变更，
/// 历史报表的口径就对不上了，而系统里没有任何地方能提示「这条曲线换过单位」。
/// </summary>
/// <param name="CoalFactor">
/// 折标煤系数，单位 kgce/计量单位。传空表示清除系数——蒸汽按焓值计价时
/// 不同批次的热值不同，用一个固定值会让报表显得精确、实际是错的。
/// </param>
public sealed record EnergyMediumFactorRequest(decimal? CoalFactor);
