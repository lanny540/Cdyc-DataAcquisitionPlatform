using CdycDataAcquisitionPlatform.Core.Domain.Entities;

namespace CdycDataAcquisitionPlatform.Infrastructure.DataAccess.Repositories;

/// <summary>
/// 定义采集数据写模型仓储。
/// </summary>
public interface ICollectionDataRecordRepository
{
    /// <summary>
    /// 添加采集数据记录。
    /// </summary>
    /// <param name="record">采集数据实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task AddAsync(CollectionDataRecord record, CancellationToken cancellationToken = default);
}
