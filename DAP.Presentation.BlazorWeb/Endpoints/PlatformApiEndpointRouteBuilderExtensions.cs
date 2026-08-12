namespace DAP.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供采集平台 API 端点映射扩展。
/// </summary>
public static class PlatformApiEndpointRouteBuilderExtensions
{
    /// <summary>
    /// 映射采集平台相关的 API 端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>当前端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapPlatformApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthEndpoints();
        endpoints.MapDashboardEndpoints();
        endpoints.MapCollectionPointEndpoints();
        endpoints.MapCollectionDataEndpoints();

        return endpoints;
    }
}
