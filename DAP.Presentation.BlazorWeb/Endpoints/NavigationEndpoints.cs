using System.Security.Claims;
using DAP.Presentation.BlazorWeb.Security;

namespace DAP.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供导航树 API 端点映射。
/// </summary>
public static class NavigationEndpoints
{
    /// <summary>
    /// 映射导航树端点。返回值按当前会话过滤：可见域、页面等级、空域隐藏规则
    /// 都在服务端判定，前端拿到什么就渲染什么，不做第二套过滤。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>当前端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapNavigationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/navigation",
                async (ClaimsPrincipal user, NavigationViewService service, CancellationToken cancellationToken) =>
                    Results.Ok(await service.BuildAsync(user, cancellationToken)))
            .WithName("GetNavigation")
            .WithTags("Navigation");

        return endpoints;
    }
}
