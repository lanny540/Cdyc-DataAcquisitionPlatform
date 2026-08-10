using CdycDataAcquisitionPlatform.Core.Domain.Entities;
using CdycDataAcquisitionPlatform.Infrastructure.DataAccess.Persistence;

namespace CdycDataAcquisitionPlatform.Infrastructure.DataAccess.Repositories;

/// <summary>
/// 提供基于 EF Core 的采集数据写模型仓储。
/// </summary>
public sealed class PostgreSqlCollectionDataRecordRepository : ICollectionDataRecordRepository
{
    private readonly DataAcquisitionPlatformDbContext _dbContext;

    /// <summary>
    /// 初始化一个新的 <see cref="PostgreSqlCollectionDataRecordRepository"/> 实例。
    /// </summary>
    /// <param name="dbContext">数据库上下文。</param>
    public PostgreSqlCollectionDataRecordRepository(DataAcquisitionPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task AddAsync(CollectionDataRecord record, CancellationToken cancellationToken = default)
    {
        return _dbContext.CollectionDataRecords.AddAsync(record, cancellationToken).AsTask();
    }
}
