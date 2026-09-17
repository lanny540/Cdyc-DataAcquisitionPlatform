using DAP.Core.Domain.Entities;

namespace DAP.Infrastructure.DataAccess.Repositories;

/// <summary>
/// 定义后台数据定义写模型仓储。
/// </summary>
public interface IManagedDataDefinitionRepository
{
    /// <summary>
    /// 根据标识获取后台数据定义。
    /// </summary>
    Task<ManagedDataDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据编码获取后台数据定义。
    /// </summary>
    Task<ManagedDataDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加后台数据定义。
    /// </summary>
    Task AddAsync(ManagedDataDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除后台数据定义。
    /// </summary>
    void Remove(ManagedDataDefinition definition);
}
