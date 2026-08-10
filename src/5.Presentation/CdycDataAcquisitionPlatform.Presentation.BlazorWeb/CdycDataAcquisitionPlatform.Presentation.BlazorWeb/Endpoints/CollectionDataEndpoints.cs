using CdycDataAcquisitionPlatform.Core.Domain.Services;
using CdycDataAcquisitionPlatform.Core.Shared.Contracts;

namespace CdycDataAcquisitionPlatform.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供采集数据相关 API 端点映射。
/// </summary>
public static class CollectionDataEndpoints
{
    /// <summary>
    /// 映射采集数据相关 API 端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>当前端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapCollectionDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var dataGroup = endpoints.MapGroup("/api/collection-data").WithTags("CollectionData");
        dataGroup.MapGet(
            "/",
            async (int? limit, IDataAcquisitionPlatformService platformService, CancellationToken cancellationToken) =>
            {
                var records = await platformService.GetCollectionDataAsync(limit ?? 20, cancellationToken);
                return Results.Ok(records);
            })
            .WithName("GetCollectionData");

        dataGroup.MapPost(
            "/ingest",
            async (IngestCollectionDataRequest request, IDataAcquisitionPlatformService platformService, CancellationToken cancellationToken) =>
            {
                var validationError = ValidateIngestCollectionDataRequest(request);
                if (validationError is not null)
                {
                    return validationError;
                }

                var savedRecord = await platformService.IngestCollectionDataAsync(request, cancellationToken);
                return Results.Ok(savedRecord);
            })
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
