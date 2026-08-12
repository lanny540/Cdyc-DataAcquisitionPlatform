namespace DAP.Core.Shared.Contracts;

/// <summary>
/// 表示服务端采集点的读模型。
/// </summary>
/// <param name="Id">采集点标识。</param>
/// <param name="Code">采集点编码。</param>
/// <param name="Name">采集点名称。</param>
/// <param name="Protocol">采集协议。</param>
/// <param name="Endpoint">采集端点或地址。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="CommunicationStatus">通信状态。</param>
/// <param name="Source">配置来源，例如 Server 或 Local。</param>
/// <param name="LastError">最近一次错误信息。</param>
/// <param name="UpdatedAt">最后更新时间。</param>
public sealed record CollectionPointDto(
    Guid Id,
    string Code,
    string Name,
    string Protocol,
    string Endpoint,
    bool IsEnabled,
    string CommunicationStatus,
    string Source,
    string? LastError,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 表示采集点新增或编辑请求。
/// </summary>
/// <param name="Id">采集点标识，为空时表示新增。</param>
/// <param name="Code">采集点编码。</param>
/// <param name="Name">采集点名称。</param>
/// <param name="Protocol">采集协议。</param>
/// <param name="Endpoint">采集端点或地址。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="Source">配置来源。</param>
public sealed record CollectionPointUpsertRequest(
    Guid? Id,
    string Code,
    string Name,
    string Protocol,
    string Endpoint,
    bool IsEnabled,
    string Source);

/// <summary>
/// 表示客户端本地保存的采集点配置。
/// </summary>
/// <param name="LocalId">本地主键。</param>
/// <param name="Code">采集点编码。</param>
/// <param name="Name">采集点名称。</param>
/// <param name="Protocol">采集协议。</param>
/// <param name="Endpoint">采集端点或地址。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="SyncStatus">同步状态。</param>
/// <param name="UpdatedAt">最后更新时间。</param>
public sealed record LocalCollectionPointDto(
    Guid LocalId,
    string Code,
    string Name,
    string Protocol,
    string Endpoint,
    bool IsEnabled,
    string SyncStatus,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 表示采集数据写入请求。
/// </summary>
/// <param name="CollectionPointCode">采集点编码。</param>
/// <param name="MetricName">指标名称。</param>
/// <param name="Value">指标值。</param>
/// <param name="Unit">单位。</param>
/// <param name="CollectedAt">采集时间。</param>
public sealed record IngestCollectionDataRequest(
    string CollectionPointCode,
    string MetricName,
    decimal Value,
    string Unit,
    DateTimeOffset CollectedAt);

/// <summary>
/// 表示采集数据读模型。
/// </summary>
/// <param name="Id">数据标识。</param>
/// <param name="CollectionPointId">采集点标识。</param>
/// <param name="CollectionPointCode">采集点编码。</param>
/// <param name="CollectionPointName">采集点名称。</param>
/// <param name="MetricName">指标名称。</param>
/// <param name="Value">指标值。</param>
/// <param name="Unit">单位。</param>
/// <param name="CollectedAt">采集时间。</param>
public sealed record CollectionDataRecordDto(
    Guid Id,
    Guid CollectionPointId,
    string CollectionPointCode,
    string CollectionPointName,
    string MetricName,
    decimal Value,
    string Unit,
    DateTimeOffset CollectedAt);

/// <summary>
/// 表示本地采集点同步请求。
/// </summary>
/// <param name="Points">需要同步的本地采集点集合。</param>
public sealed record SyncCollectionPointsRequest(IReadOnlyCollection<LocalCollectionPointDto> Points);

/// <summary>
/// 表示本地采集点同步结果。
/// </summary>
/// <param name="CreatedCount">新建数量。</param>
/// <param name="UpdatedCount">更新数量。</param>
/// <param name="SyncedAt">同步完成时间。</param>
/// <param name="SyncedIds">已同步的采集点标识。</param>
public sealed record SyncCollectionPointsResponse(
    int CreatedCount,
    int UpdatedCount,
    DateTimeOffset SyncedAt,
    IReadOnlyCollection<Guid> SyncedIds);

/// <summary>
/// 表示平台概览数据。
/// </summary>
/// <param name="TotalCollectionPoints">采集点总数。</param>
/// <param name="OnlineCollectionPoints">在线采集点数量。</param>
/// <param name="OfflineCollectionPoints">离线采集点数量。</param>
/// <param name="LocalSourcedCollectionPoints">来源于客户端同步的采集点数量。</param>
/// <param name="LatestRecords">最新采集数据。</param>
/// <param name="CollectionPoints">采集点状态快照。</param>
/// <param name="SprintLanes">看板列数据。</param>
/// <param name="ActiveMembers">活跃成员数据。</param>
public sealed record DashboardOverviewDto(
    int TotalCollectionPoints,
    int OnlineCollectionPoints,
    int OfflineCollectionPoints,
    int LocalSourcedCollectionPoints,
    IReadOnlyCollection<CollectionDataRecordDto> LatestRecords,
    IReadOnlyCollection<CollectionPointDto> CollectionPoints,
    IReadOnlyCollection<SprintLaneDto>? SprintLanes = null,
    IReadOnlyCollection<MemberAvatarDto>? ActiveMembers = null);

public sealed record SprintLaneDto(
    string Title,
    string Summary,
    IReadOnlyCollection<SprintTaskCardDto> Tasks);

public sealed record SprintTaskCardDto(
    string Title,
    string Description,
    string Stage,
    string Tag,
    string TagBackground,
    string TagForeground,
    string Ticket,
    string Metric,
    string AccentBrush,
    int Progress,
    string AssigneeInitials,
    string OwnerName,
    string OwnerRole,
    IReadOnlyCollection<InspectorMetricDto> InspectorMetrics,
    IReadOnlyCollection<ActivityLogDto> ActivityLogs);

public sealed record MemberAvatarDto(string Initials, string AccentBrush);

public sealed record InspectorMetricDto(string Label, string Value, string AccentBrush);

public sealed record ActivityLogDto(string Time, string Message, string AccentBrush);
