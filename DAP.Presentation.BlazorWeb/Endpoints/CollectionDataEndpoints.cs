using DAP.Core.Domain.Services;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Security;

namespace DAP.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供采集数据相关 API 端点映射。
/// </summary>
public static class CollectionDataEndpoints
{
    /// <summary>
    /// 单次查询返回的最大记录数。
    /// </summary>
    private const int MaxQueryLimit = 500;

    /// <summary>
    /// 映射采集数据相关 API 端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>当前端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapCollectionDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder dataGroup = endpoints.MapGroup("/api/collection-data").WithTags("CollectionData")
            .RequireBanknotePaperDomain();
        dataGroup.MapGet(
                "/",
                async (int? limit, IDataAcquisitionPlatformService platformService,
                    CancellationToken cancellationToken) =>
                {
                    // 上限 500：不夹取时 ?limit=99999999 等于一次拉全表，
                    // 既压数据库也压浏览器（这块数据只用于概览与最近值）。
                    var effectiveLimit = Math.Clamp(limit ?? 20, 1, MaxQueryLimit);

                    IReadOnlyCollection<CollectionDataRecordDto> records =
                        await platformService.GetCollectionDataAsync(effectiveLimit, cancellationToken);
                    return Results.Ok(records);
                })
            .WithName("GetCollectionData");

        dataGroup.MapPost(
                "/ingest",
                async (IngestCollectionDataRequest request, IDataAcquisitionPlatformService platformService,
                    CancellationToken cancellationToken) =>
                {
                    IResult? validationError = ValidateIngestCollectionDataRequest(request);
                    if (validationError is not null)
                    {
                        return validationError;
                    }

                    CollectionDataRecordDto savedRecord =
                        await platformService.IngestCollectionDataAsync(request, cancellationToken);
                    return Results.Ok(savedRecord);
                })
            // 上报属于写操作：只要求钞纸域时，任何普通查看者（含车间只读终端）
            // 都能伪造测量值，这些值会直接进报表与能耗计算。
            // 采集端接入需要独立凭据（API Key）后再放开，不能复用人员权限。
            .RequirePermission(AuthorizationCatalog.SystemManagePermission)
            .WithName("IngestCollectionData");

        return endpoints;
    }

    private static IResult? ValidateIngestCollectionDataRequest(IngestCollectionDataRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CollectionPointCode) ||
            string.IsNullOrWhiteSpace(request.MetricName))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["CollectionData"] = ["采集点编码和指标名称不能为空。"]
            });
        }

        return null;
    }
}
