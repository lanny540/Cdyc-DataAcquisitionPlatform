using DAP.Core.Shared.Contracts;

namespace DAP.Core.Domain.Services;

/// <summary>
/// 定义采集平台在服务端对外提供的核心业务能力。
/// </summary>
public interface IDataAcquisitionPlatformService
{
    /// <summary>
    /// 获取平台概览数据。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>概览结果。</returns>
    Task<DashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有采集点配置。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>采集点集合。</returns>
    Task<IReadOnlyCollection<CollectionPointDto>> GetCollectionPointsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 新增或更新采集点配置。
    /// </summary>
    /// <param name="request">采集点请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>保存后的采集点。</returns>
    Task<CollectionPointDto> UpsertCollectionPointAsync(
        CollectionPointUpsertRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定采集点配置。
    /// </summary>
    /// <param name="id">采集点标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除是否成功。</returns>
    Task<bool> DeleteCollectionPointAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取最新采集数据。
    /// </summary>
    /// <param name="limit">返回数量上限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>采集数据集合。</returns>
    Task<IReadOnlyCollection<CollectionDataRecordDto>> GetCollectionDataAsync(
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 接收外部设备采集到的数据并写入服务端。
    /// </summary>
    /// <param name="request">采集数据请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写入后的数据记录。</returns>
    Task<CollectionDataRecordDto> IngestCollectionDataAsync(
        IngestCollectionDataRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 将客户端本地配置同步到服务端。
    /// </summary>
    /// <param name="request">同步请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步结果。</returns>
    Task<SyncCollectionPointsResponse> SyncLocalCollectionPointsAsync(
        SyncCollectionPointsRequest request,
        CancellationToken cancellationToken = default);
}
