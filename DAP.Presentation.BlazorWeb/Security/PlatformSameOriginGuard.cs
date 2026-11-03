namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 变更类请求的同源校验，作为 Cookie 会话的 CSRF 第二道防线。
///
/// 第一道是 <c>SameSite=Lax</c>：跨站的表单 POST 不会带上会话 Cookie，
/// 所以「拿别人身份改密码」这类攻击在现代浏览器上本来就发不起来。
/// 这一层解决的是 SameSite 被削弱后的场景——比如将来为了嵌 iframe 改成 None，
/// 或者遇到不严格实现 SameSite 的旧浏览器。两道都失效时，Origin 校验仍然拦得住。
///
/// 为什么不用防伪令牌：登录页与改密页是 Blazor InteractiveAuto 组件，
/// 令牌要跨「SSR 预渲染 → WASM 水合」两段存活，还得为客户端渲染单独铺一套下发通道。
/// 同源校验在同一层拿到同样的结论，且不依赖页面怎么渲染。
/// </summary>
public static class PlatformSameOriginGuard
{
    /// <summary>
    /// 豁免的路径：OIDC 回调是 IdP 用 form_post 跨站 POST 回来的，来源天然是外站。
    /// </summary>
    private static readonly string[] ExemptPrefixes = ["/api/auth/oidc/"];

    /// <summary>
    /// 对变更类请求（POST/PUT/PATCH/DELETE）校验来源与站点一致。
    /// </summary>
    /// <param name="app">应用构建器。</param>
    /// <returns>当前应用构建器。</returns>
    public static IApplicationBuilder UsePlatformSameOriginGuard(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (!RequiresCheck(context.Request))
            {
                await next(context);
                return;
            }

            if (IsSameOrigin(context.Request))
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new {message = "请求来源与本站不一致，已拒绝执行。"});
        });
    }

    /// <summary>
    /// 判断某个请求是否需要做同源校验。
    ///
    /// 覆盖范围是「**所有**非只读的 /api 请求」，而不是逐个登记前缀：
    /// 用白名单时，新加一个写端点只要忘了往名单里补一行，这道防线就静默失效，
    /// 而且没有任何报错。只读方法（GET/HEAD/OPTIONS）不校验——它们本就不改状态，
    /// 也不该被跨站图片/预取之类误伤。
    /// </summary>
    /// <param name="request">HTTP 请求。</param>
    /// <returns>需要校验时 true。</returns>
    public static bool RequiresCheck(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method))
        {
            return false;
        }

        var path = request.Path.Value ?? string.Empty;

        if (ExemptPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return path.StartsWith("/api", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断请求是否来自本站。
    ///
    /// 判定顺序：有 <c>Origin</c> 就只看 Origin（浏览器对跨站 POST 一定带它）；
    /// 没有则退回 <c>Referer</c>；两者都没有时放行——非浏览器客户端（curl、健康检查、
    /// 未来的服务间调用）本来就不带这两个头，拦住它们只会挡住自己人。
    /// 真正的跨站攻击一定由浏览器发起，浏览器一定会带 Origin。
    /// </summary>
    /// <param name="request">HTTP 请求。</param>
    /// <returns>来源与本站一致时 true。</returns>
    public static bool IsSameOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();

        if (!string.IsNullOrEmpty(origin))
        {
            return MatchesHost(origin, request);
        }

        var referer = request.Headers.Referer.ToString();

        return string.IsNullOrEmpty(referer) || MatchesHost(referer, request);
    }

    private static bool MatchesHost(string value, HttpRequest request)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
               && (request.Host.Port is null || uri.Port == request.Host.Port);
    }
}
