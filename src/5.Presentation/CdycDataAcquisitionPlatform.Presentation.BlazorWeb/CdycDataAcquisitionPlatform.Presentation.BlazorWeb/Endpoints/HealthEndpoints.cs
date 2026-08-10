namespace CdycDataAcquisitionPlatform.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供健康检查相关 API 端点映射。
/// </summary>
public static class HealthEndpoints
{
    /// <summary>
    /// 映射健康检查相关 API 端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>当前端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTimeOffset.UtcNow }))
            .WithName("GetHealth");

        return endpoints;
    }
}
