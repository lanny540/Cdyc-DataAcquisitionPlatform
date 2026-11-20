using DAP.Core.Domain.Entities;
using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DAP.Infrastructure.DataAccess.Repositories;

/// <summary>
/// 提供基于 EF Core 的采集点写模型仓储。
/// </summary>
public sealed class PostgreSqlCollectionPointRepository : ICollectionPointRepository
{
    private readonly DataAcquisitionPlatformDbContext _dbContext;

    /// <summary>
    /// 初始化一个新的 <see cref="PostgreSqlCollectionPointRepository"/> 实例。
    /// </summary>
    /// <param name="dbContext">数据库上下文。</param>
    public PostgreSqlCollectionPointRepository(DataAcquisitionPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<CollectionPoint?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.CollectionPoints
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CollectionPoint?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return _dbContext.CollectionPoints
            .FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
    }

    /// <inheritdoc />
    public Task AddAsync(CollectionPoint collectionPoint, CancellationToken cancellationToken = default)
    {
        return _dbContext.CollectionPoints.AddAsync(collectionPoint, cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public void Remove(CollectionPoint collectionPoint)
    {
        _dbContext.CollectionPoints.Remove(collectionPoint);
    }
}
