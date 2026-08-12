using DAP.Core.Shared.Contracts;

namespace DAP.Infrastructure.DataAccess.Repositories;

/// <summary>
/// 定义平台只读查询仓储。
/// </summary>
public interface IPlatformReadRepository
{
    /// <summary>
    /// 获取采集点列表。
    /// </summary>
    Task<IReadOnlyCollection<CollectionPointDto>> GetCollectionPointsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取采集数据列表。
    /// </summary>
    Task<IReadOnlyCollection<CollectionDataRecordDto>> GetCollectionDataAsync(int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取平台概览。
    /// </summary>
    Task<DashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default);
}
