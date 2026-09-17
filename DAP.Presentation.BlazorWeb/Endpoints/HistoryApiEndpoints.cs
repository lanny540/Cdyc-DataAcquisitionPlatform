using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;

namespace DAP.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供 History 数据库测试相关 API 端点映射。
/// </summary>
public static class HistoryApiEndpoints
{
    /// <summary>
    /// 映射 History 数据库测试相关 API 端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>当前端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapHistoryApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder historyGroup = endpoints.MapGroup("/api/history-api").WithTags("HistoryApi");

        historyGroup.MapPost(
                "/current-value",
                async (HistoryCurrentValueQueryRequest request, IHistoryApiProxyService historyApiProxyService,
                    CancellationToken cancellationToken) =>
                {
                    IResult? validationError = ValidateCurrentValueRequest(request);
                    if (validationError is not null)
                    {
                        return validationError;
                    }

                    HistoryApiQueryResponse response =
                        await historyApiProxyService.GetCurrentValueAsync(request, cancellationToken);
                    return Results.Ok(response);
                })
            .WithName("GetHistoryCurrentValue");

        historyGroup.MapPost(
                "/raw-data",
                async (HistoryRawDataQueryRequest request, IHistoryApiProxyService historyApiProxyService,
                    CancellationToken cancellationToken) =>
                {
                    IResult? validationError = ValidateRawDataRequest(request);
                    if (validationError is not null)
                    {
                        return validationError;
                    }

                    HistoryApiQueryResponse response =
                        await historyApiProxyService.GetRawDataAsync(request, cancellationToken);
                    return Results.Ok(response);
                })
            .WithName("GetHistoryRawData");

        return endpoints;
    }

    private static IResult? ValidateCurrentValueRequest(HistoryCurrentValueQueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TagNames))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["HistoryApi"] = ["标签名称不能为空。"]
            });
        }

        return null;
    }

    private static IResult? ValidateRawDataRequest(HistoryRawDataQueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TagName))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["HistoryApi"] = ["标签名称不能为空。"]
            });
        }

        if (request.EndTime <= request.StartTime)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["HistoryApi"] = ["结束时间必须晚于开始时间。"]
            });
        }

        if (request.StartIndex < 0 || request.Count <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["HistoryApi"] = ["起始偏移量不能小于 0，返回条数必须大于 0。"]
            });
        }

        return null;
    }
}
