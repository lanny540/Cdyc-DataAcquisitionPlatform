namespace DAP.Core.Shared.Contracts;

/// <summary>
/// 表示后台维护的数据定义。
/// </summary>
/// <param name="Id">数据定义标识。</param>
/// <param name="Code">数据编码。</param>
/// <param name="Name">数据名称。</param>
/// <param name="AcquisitionType">采集方式，例如 Modbus、Historian API。</param>
/// <param name="ConnectionAddress">连接地址或来源地址。</param>
/// <param name="Identifier">数据标识，例如寄存器地址、Tag 名称、Topic。</param>
/// <param name="Department">归属部门。</param>
/// <param name="ProcessCode">工序编码。</param>
/// <param name="DataCategory">数据类别，例如电力数据、蒸汽数据。</param>
/// <param name="Unit">计量单位。</param>
/// <param name="Description">补充说明。</param>
/// <param name="ConfigurationJson">协议配置 JSON。</param>
/// <param name="BusinessTagsJson">业务标识 JSON。</param>
/// <param name="CollectionIntervalSeconds">采集频次，单位为秒。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="UpdatedAt">最后更新时间。</param>
public sealed record ManagedDataDefinitionDto(
    Guid Id,
    string Code,
    string Name,
    string AcquisitionType,
    string ConnectionAddress,
    string Identifier,
    string Department,
    string ProcessCode,
    string DataCategory,
    string Unit,
    string Description,
    string ConfigurationJson,
    string BusinessTagsJson,
    int CollectionIntervalSeconds,
    bool IsEnabled,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 表示后台数据定义的新增或更新请求。
/// </summary>
/// <param name="Id">数据定义标识，为空时表示新增。</param>
/// <param name="Code">数据编码。</param>
/// <param name="Name">数据名称。</param>
/// <param name="AcquisitionType">采集方式。</param>
/// <param name="ConnectionAddress">连接地址或来源地址。</param>
/// <param name="Identifier">数据标识。</param>
/// <param name="Department">归属部门。</param>
/// <param name="ProcessCode">工序编码。</param>
/// <param name="DataCategory">数据类别。</param>
/// <param name="Unit">计量单位。</param>
/// <param name="Description">补充说明。</param>
/// <param name="ConfigurationJson">协议配置 JSON。</param>
/// <param name="BusinessTagsJson">业务标识 JSON。</param>
/// <param name="CollectionIntervalSeconds">采集频次，单位为秒。</param>
/// <param name="IsEnabled">是否启用。</param>
public sealed record ManagedDataDefinitionUpsertRequest(
    Guid? Id,
    string Code,
    string Name,
    string AcquisitionType,
    string ConnectionAddress,
    string Identifier,
    string Department,
    string ProcessCode,
    string DataCategory,
    string Unit,
    string Description,
    string ConfigurationJson,
    string BusinessTagsJson,
    int CollectionIntervalSeconds,
    bool IsEnabled);

/// <summary>
/// 表示后台数据节点详情。
/// </summary>
/// <param name="Definition">后台数据定义。</param>
/// <param name="CollectionPoint">已落库的采集点快照。</param>
/// <param name="CurrentRecord">当前最新采集值。</param>
/// <param name="RecentRecords">最近历史采集值。</param>
public sealed record ManagedDataDefinitionDetailsDto(
    ManagedDataDefinitionDto Definition,
    CollectionPointDto? CollectionPoint,
    CollectionDataRecordDto? CurrentRecord,
    IReadOnlyCollection<CollectionDataRecordDto> RecentRecords);
