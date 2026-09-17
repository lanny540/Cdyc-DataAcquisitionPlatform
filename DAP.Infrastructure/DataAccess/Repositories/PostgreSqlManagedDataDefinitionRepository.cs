using DAP.Core.Domain.Entities;
using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DAP.Infrastructure.DataAccess.Repositories;

/// <summary>
/// 提供基于 EF Core 的后台数据定义写模型仓储。
/// </summary>
public sealed class PostgreSqlManagedDataDefinitionRepository : IManagedDataDefinitionRepository
{
    private readonly DataAcquisitionPlatformDbContext _dbContext;

    /// <summary>
    /// 初始化一个新的 <see cref="PostgreSqlManagedDataDefinitionRepository"/> 实例。
    /// </summary>
    public PostgreSqlManagedDataDefinitionRepository(DataAcquisitionPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<ManagedDataDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.ManagedDataDefinitions
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public Task<ManagedDataDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return _dbContext.ManagedDataDefinitions
            .FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
    }

    public Task AddAsync(ManagedDataDefinition definition, CancellationToken cancellationToken = default)
    {
        return _dbContext.ManagedDataDefinitions.AddAsync(definition, cancellationToken).AsTask();
    }

    public void Remove(ManagedDataDefinition definition)
    {
        _dbContext.ManagedDataDefinitions.Remove(definition);
    }
}
