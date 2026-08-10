namespace CdycDataAcquisitionPlatform.Core.Domain.Entities;

/// <summary>
/// 表示服务端保存的采集点配置。
/// </summary>
public sealed class CollectionPoint
{
    /// <summary>
    /// 获取或设置采集点标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置采集点编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置采集点名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置采集协议。
    /// </summary>
    public string Protocol { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置采集端点或地址。
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置是否启用。
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 获取或设置通信状态。
    /// </summary>
    public string CommunicationStatus { get; set; } = "未知";

    /// <summary>
    /// 获取或设置配置来源。
    /// </summary>
    public string Source { get; set; } = "Server";

    /// <summary>
    /// 获取或设置最近一次错误信息。
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// 获取或设置最后更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// 获取或设置采集数据记录集合。
    /// </summary>
    public ICollection<CollectionDataRecord> Records { get; set; } = [];
}

/// <summary>
/// 表示单条采集数据。
/// </summary>
public sealed class CollectionDataRecord
{
    /// <summary>
    /// 获取或设置数据标识。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 获取或设置采集点标识。
    /// </summary>
    public Guid CollectionPointId { get; set; }

    /// <summary>
    /// 获取或设置指标名称。
    /// </summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置指标值。
    /// </summary>
    public decimal Value { get; set; }

    /// <summary>
    /// 获取或设置单位。
    /// </summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置采集时间。
    /// </summary>
    public DateTimeOffset CollectedAt { get; set; }

    /// <summary>
    /// 获取或设置所属采集点。
    /// </summary>
    public CollectionPoint? CollectionPoint { get; set; }
}
