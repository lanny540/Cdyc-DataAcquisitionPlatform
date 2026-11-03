using DAP.Presentation.BlazorWeb.Security.Auditing;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 把领域服务装饰器抛出的 <see cref="ForbiddenAccessException"/> 统一映射为
/// API 403 或页面跳转 /forbidden（docs/AUTH_DESIGN.md 7.2）。
/// </summary>
public static class ForbiddenAccessExtensions
{
    /// <summary>
    /// 注册越权异常处理中间件。必须包住端点映射与 Blazor 电路，因此放在管道靠前位置。
    /// </summary>
    /// <param name="app">应用构建器。</param>
    /// <returns>当前应用构建器。</returns>
    public static IApplicationBuilder UseForbiddenAccessHandling(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (ForbiddenAccessException ex)
            {
                if (context.Response.HasStarted)
                {
                    // 电路帧已经开始下发，无法改写响应：只能终止连接由客户端报错。
                    throw;
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;

                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    await context.Response.WriteAsJsonAsync(new {message = ex.Message});
                    return;
                }

                context.Response.Redirect("/forbidden");
            }
        });
    }
}
