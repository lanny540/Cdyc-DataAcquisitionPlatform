using DAP.Core.Domain.Services;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DAP.Presentation.BlazorWeb.Security.Auditing;

/// <summary>
/// 采集配置（采集点、后台数据定义）的授权与审计装饰器（docs/AUTH_DESIGN.md 7.2 / 9.4）。
///
/// 配置类写操作要求平台配置权限点；业务数据写入（采集上报、本地同步）不属于
/// 配置操作，原样转发不留痕。读方法同样直接转发。
/// </summary>
public sealed class AuditedDataAcquisitionService(
    IDataAcquisitionPlatformService inner,
    IServiceScopeFactory scopeFactory) : IDataAcquisitionPlatformService
{
    /// <inheritdoc />
    public Task<DashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default)
    {
        return inner.GetDashboardOverviewAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<CollectionPointDto>> GetCollectionPointsAsync(
        CancellationToken cancellationToken = default)
    {
        return inner.GetCollectionPointsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<CollectionPointDto> UpsertCollectionPointAsync(
        CollectionPointUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        return ServiceAuditGuard.ExecuteAsync(
            scopeFactory,
            AuthorizationCatalog.SystemManagePermission,
            "collection_point.upsert",
            "collection-point",
            request.Id?.ToString(),
            request.Name,
            _ => inner.UpsertCollectionPointAsync(request, cancellationToken),
            detailFactory: result => new {result.Id, result.Name, result.IsEnabled},
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteCollectionPointAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return ServiceAuditGuard.ExecuteAsync(
            scopeFactory,
            AuthorizationCatalog.SystemManagePermission,
            "collection_point.delete",
            "collection-point",
            id.ToString(),
            null,
            _ => inner.DeleteCollectionPointAsync(id, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<ManagedDataDefinitionDto>> GetManagedDataDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        return inner.GetManagedDataDefinitionsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<ManagedDataDefinitionDetailsDto?> GetManagedDataDefinitionDetailsAsync(
        Guid id,
        int historyLimit = 50,
        CancellationToken cancellationToken = default)
    {
        return inner.GetManagedDataDefinitionDetailsAsync(id, historyLimit, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ManagedDataDefinitionDto> UpsertManagedDataDefinitionAsync(
        ManagedDataDefinitionUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        return ServiceAuditGuard.ExecuteAsync(
            scopeFactory,
            AuthorizationCatalog.SystemManagePermission,
            "managed_data.upsert",
            "managed-data-definition",
            request.Id?.ToString(),
            request.Name,
            _ => inner.UpsertManagedDataDefinitionAsync(request, cancellationToken),
            detailFactory: result => new {result.Id, result.Name},
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteManagedDataDefinitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return ServiceAuditGuard.ExecuteAsync(
            scopeFactory,
            AuthorizationCatalog.SystemManagePermission,
            "managed_data.delete",
            "managed-data-definition",
            id.ToString(),
            null,
            _ => inner.DeleteManagedDataDefinitionAsync(id, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<CollectionDataRecordDto>> GetCollectionDataAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        return inner.GetCollectionDataAsync(limit, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CollectionDataRecordDto> IngestCollectionDataAsync(
        IngestCollectionDataRequest request,
        CancellationToken cancellationToken = default)
    {
        // 业务数据写入：由采集端自动产生，不属于配置类写操作，不审计。
        return inner.IngestCollectionDataAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SyncCollectionPointsResponse> SyncLocalCollectionPointsAsync(
        SyncCollectionPointsRequest request,
        CancellationToken cancellationToken = default)
    {
        // 客户端本地配置同步：走应用自身的同步协议，不审计。
        return inner.SyncLocalCollectionPointsAsync(request, cancellationToken);
    }
}
