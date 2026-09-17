using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;

namespace DAP.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供连接诊断相关 API 端点映射。
/// </summary>
public static class ConnectionDiagnosticsEndpoints
{
    /// <summary>
    /// 映射连接诊断相关 API 端点。
    /// </summary>
    public static IEndpointRouteBuilder MapConnectionDiagnosticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/server-connections/status",
                async (IConnectionDiagnosticsService diagnosticsService, CancellationToken cancellationToken) =>
                {
                    IReadOnlyCollection<ServerConnectionStatusDto> statuses =
                        await diagnosticsService.GetServerStatusesAsync(cancellationToken);
                    return Results.Ok(statuses);
                })
            .WithName("GetServerConnectionStatuses")
            .WithTags("ConnectionDiagnostics");

        endpoints.MapPost(
                "/api/managed-data-definitions/test-connection",
                async (ManagedDataDefinitionUpsertRequest request, IConnectionDiagnosticsService diagnosticsService,
                    CancellationToken cancellationToken) =>
                {
                    ManagedDataConnectionTestResultDto result =
                        await diagnosticsService.TestManagedDataConnectionAsync(request, cancellationToken);
                    return Results.Ok(result);
                })
            .WithName("TestManagedDataConnection")
            .WithTags("ConnectionDiagnostics");

        return endpoints;
    }
}
