namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 登录回跳地址校验。外部 IdP 回调、表单登录、挑战发起三处都走这一份实现——
/// 任何一处漏判都会把登录页变成开放重定向跳板。
/// </summary>
public static class PlatformReturnUrl
{
    /// <summary>
    /// 计算登录后的跳转目标：优先回跳原始地址，但只接受站内相对地址。
    /// </summary>
    /// <param name="returnUrl">原始回跳地址。</param>
    /// <param name="landingPage">回跳不可用时的落地页。</param>
    /// <returns>最终跳转目标。</returns>
    public static string Resolve(string? returnUrl, string landingPage)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return landingPage;
        }

        var candidate = returnUrl.Trim();

        // 只接受以单个 / 开头的站内路径：//evil.com 与 /\evil.com 都是协议相对地址，
        // 浏览器会当成外站，等于开放重定向。
        if (!candidate.StartsWith('/') || candidate.StartsWith("//", StringComparison.Ordinal)
            || candidate.StartsWith("/\\", StringComparison.Ordinal))
        {
            return landingPage;
        }

        // 登录页与认证接口自身不能作为回跳目标，否则会形成重定向环。
        if (candidate.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
        {
            return landingPage;
        }

        return candidate;
    }
}
