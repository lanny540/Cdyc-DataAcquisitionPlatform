using CdycDataAcquisitionPlatform.Core.Domain.Entities;

namespace CdycDataAcquisitionPlatform.Infrastructure.DataAccess.Repositories;

/// <summary>
/// 定义采集点写模型仓储。
/// </summary>
public interface ICollectionPointRepository
{
    /// <summary>
    /// 根据标识获取采集点。
    /// </summary>
    /// <param name="id">采集点标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>采集点实体。</returns>
    Task<CollectionPoint?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据编码获取采集点。
    /// </summary>
    /// <param name="code">采集点编码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>采集点实体。</returns>
    Task<CollectionPoint?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加采集点。
    /// </summary>
    /// <param name="collectionPoint">采集点实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task AddAsync(CollectionPoint collectionPoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除采集点。
    /// </summary>
    /// <param name="collectionPoint">采集点实体。</param>
    void Remove(CollectionPoint collectionPoint);
}
