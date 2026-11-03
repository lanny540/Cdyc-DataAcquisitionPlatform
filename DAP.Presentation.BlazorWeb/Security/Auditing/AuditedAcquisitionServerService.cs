using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DAP.Presentation.BlazorWeb.Security.Auditing;

/// <summary>
/// 采集服务器配置的授权与审计装饰器（docs/AUTH_DESIGN.md 7.2 / 9.4）。
/// 增删改要求平台配置权限点；探测（连通性测试）是只读诊断，原样转发。
/// </summary>
public sealed class AuditedAcquisitionServerService(
    IAcquisitionServerService inner,
    IServiceScopeFactory scopeFactory) : IAcquisitionServerService
{
    /// <inheritdoc />
    public Task<IReadOnlyCollection<AcquisitionServerDto>> GetServersAsync(
        CancellationToken cancellationToken = default)
    {
        return inner.GetServersAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<AcquisitionServerDto?> GetServerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return inner.GetServerAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<AcquisitionServerDto> UpsertServerAsync(
        AcquisitionServerUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        return ServiceAuditGuard.ExecuteAsync(
            scopeFactory,
            AuthorizationCatalog.SystemManagePermission,
            "acquisition_server.upsert",
            "acquisition-server",
            request.Id?.ToString(),
            request.DisplayName,
            _ => inner.UpsertServerAsync(request, cancellationToken),
            detailFactory: result => new {result.Id, result.DisplayName},
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<(bool Succeeded, string? ErrorMessage)> DeleteServerAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return ServiceAuditGuard.ExecuteAsync(
            scopeFactory,
            AuthorizationCatalog.SystemManagePermission,
            "acquisition_server.delete",
            "acquisition-server",
            id.ToString(),
            null,
            _ => inner.DeleteServerAsync(id, cancellationToken),
            detailFactory: result => new {result.Succeeded, result.ErrorMessage},
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<AcquisitionServerProbeResultDto> TestServerAsync(
        AcquisitionServerUpsertRequest request,
        string? identifier = null,
        string? requestPath = null,
        IReadOnlyDictionary<string, string>? nodeConfiguration = null,
        CancellationToken cancellationToken = default)
    {
        return inner.TestServerAsync(request, identifier, requestPath, nodeConfiguration, cancellationToken);
    }
}
