using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;

namespace DAP.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供采集点相关 API 端点映射。
/// </summary>
public static class CollectionPointEndpoints
{
    /// <summary>
    /// 映射采集点相关 API 端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>当前端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapCollectionPointEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var pointGroup = endpoints.MapGroup("/api/collection-points").WithTags("CollectionPoints");
        pointGroup.MapGet(
            "/",
            async (IDataAcquisitionPlatformService platformService, CancellationToken cancellationToken) =>
            {
                var points = await platformService.GetCollectionPointsAsync(cancellationToken);
                return Results.Ok(points);
            })
            .WithName("GetCollectionPoints");

        pointGroup.MapPost(
            "/",
            async (CollectionPointUpsertRequest request, IDataAcquisitionPlatformService platformService, CancellationToken cancellationToken) =>
            {
                var validationError = ValidateCollectionPointRequest(request);
                if (validationError is not null)
                {
                    return validationError;
                }

                var savedPoint = await platformService.UpsertCollectionPointAsync(request, cancellationToken);
                return Results.Ok(savedPoint);
            })
            .WithName("UpsertCollectionPoint");

        pointGroup.MapDelete(
            "/{id:guid}",
            async (Guid id, IDataAcquisitionPlatformService platformService, CancellationToken cancellationToken) =>
            {
                var deleted = await platformService.DeleteCollectionPointAsync(id, cancellationToken);
                return deleted ? Results.NoContent() : Results.NotFound();
            })
            .WithName("DeleteCollectionPoint");

        pointGroup.MapPost(
            "/sync",
            async (SyncCollectionPointsRequest request, IDataAcquisitionPlatformService platformService, CancellationToken cancellationToken) =>
            {
                var result = await platformService.SyncLocalCollectionPointsAsync(request, cancellationToken);
                return Results.Ok(result);
            })
            .WithName("SyncCollectionPoints");

        return endpoints;
    }

    private static IResult? ValidateCollectionPointRequest(CollectionPointUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Protocol) ||
            string.IsNullOrWhiteSpace(request.Endpoint))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["CollectionPoint"] = ["编码、名称、协议和端点不能为空。"]
            });
        }

        return null;
    }
}
