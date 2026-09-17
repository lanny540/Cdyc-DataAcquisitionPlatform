using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;

namespace DAP.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供后台数据执行相关 API 端点映射。
/// </summary>
public static class ManagedDataExecutionEndpoints
{
    /// <summary>
    /// 映射后台数据执行相关 API 端点。
    /// </summary>
    public static IEndpointRouteBuilder MapManagedDataExecutionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/managed-data-definitions").WithTags("ManagedDataExecution");

        group.MapPost(
                "/{id:guid}/debug-read",
                async (Guid id, IManagedDataExecutionService executionService, CancellationToken cancellationToken) =>
                {
                    ManagedDataExecutionResultDto result =
                        await executionService.DebugReadAsync(id, cancellationToken);
                    return Results.Ok(result);
                })
            .WithName("DebugReadManagedDataDefinition");

        group.MapPost(
                "/{id:guid}/collect",
                async (Guid id, IManagedDataExecutionService executionService, CancellationToken cancellationToken) =>
                {
                    ManagedDataExecutionResultDto result =
                        await executionService.CollectAsync(id, cancellationToken);
                    return Results.Ok(result);
                })
            .WithName("CollectManagedDataDefinition");

        return endpoints;
    }
}
