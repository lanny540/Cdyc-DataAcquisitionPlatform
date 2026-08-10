using CdycDataAcquisitionPlatform.Core.Domain.Services;

namespace CdycDataAcquisitionPlatform.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供看板相关 API 端点映射。
/// </summary>
public static class DashboardEndpoints
{
    /// <summary>
    /// 映射看板相关 API 端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>当前端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var dashboardGroup = endpoints.MapGroup("/api/dashboard").WithTags("Dashboard");
        dashboardGroup.MapGet(
            "/overview",
            async (IDataAcquisitionPlatformService platformService, CancellationToken cancellationToken) =>
            {
                var overview = await platformService.GetDashboardOverviewAsync(cancellationToken);
                return Results.Ok(overview);
            })
            .WithName("GetDashboardOverview");

        return endpoints;
    }
}
