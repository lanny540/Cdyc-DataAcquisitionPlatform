using System.Security.Claims;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Presentation.BlazorWeb.Security.External;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace DAP.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供登录、登出、改密与当前用户信息端点。
///
/// 登录与改密走原生表单提交而不是 Blazor 交互：InteractiveAuto 下页面可能落在 Server 电路里，
/// 电路内拿不到 <c>HttpContext</c>，无法签发或更新 Cookie。表单提交由浏览器直接发起请求，
/// 服务端在端点里完成签发，两条渲染路径行为一致。
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// 映射认证相关端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>当前端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/auth").WithTags("Auth");

        // 三个处理器的首个参数是 HttpContext，写成方法组会被推断成 RequestDelegate，
        // 返回值（重定向）会被丢弃。显式转成 Delegate 走路由处理器重载。
        // 登录与改密要限流：两者都会跑密码哈希校验，是可被批量触发的高开销操作。
        group.MapPost("/login", (Delegate)LoginAsync)
            .WithName("AuthLogin")
            .RequireRateLimiting(PlatformRateLimiting.CredentialPolicy)
            .DisableAntiforgery();

        group.MapPost("/logout", (Delegate)LogoutAsync)
            .WithName("AuthLogout")
            .DisableAntiforgery();

        group.MapPost("/change-password", (Delegate)ChangePasswordAsync)
            .WithName("AuthChangePassword")
            .RequireRateLimiting(PlatformRateLimiting.CredentialPolicy)
            .DisableAntiforgery();

        group.MapGet("/me", GetCurrentUserAsync)
            .WithName("AuthMe");

        // 登录页需要知道有哪些外部登录方式可用。放在 /api/auth 下即处于免登录白名单内，
        // 且只暴露显示名与编码，不含任何密钥信息。
        group.MapGet("/providers", GetExternalProviders)
            .WithName("AuthExternalProviders");

        group.MapGet("/oidc/{code}/challenge", ChallengeOidcAsync)
            .WithName("AuthOidcChallenge");

        return endpoints;
    }

    private static IResult GetExternalProviders(IOptions<PlatformAuthOptions> options)
    {
        List<OidcProviderDto> providers = options.Value.Oidc.EnabledProviders()
            .Select(provider => new OidcProviderDto(
                provider.Code,
                string.IsNullOrWhiteSpace(provider.DisplayName) ? provider.Code : provider.DisplayName,
                $"/api/auth/oidc/{provider.Code}/challenge"))
            .ToList();

        return Results.Ok(providers);
    }

    /// <summary>
    /// 发起 OIDC 授权码挑战。回跳地址随 properties 一起穿过 IdP 的往返，
    /// 回调时再从 properties 取出来——不用 Cookie 存，避免多标签页互相覆盖。
    /// </summary>
    private static async Task<IResult> ChallengeOidcAsync(
        HttpContext httpContext,
        string code,
        string? returnUrl,
        IOptions<PlatformAuthOptions> options,
        ILoggerFactory loggerFactory)
    {
        OidcProviderOptions? provider = options.Value.Oidc.EnabledProviders()
            .FirstOrDefault(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            return Results.Redirect(
                $"/login?error={Uri.EscapeDataString("该统一登录方式未启用，请使用账号密码登录。")}");
        }

        AuthenticationProperties properties = new();
        var safeReturnUrl = PlatformReturnUrl.Resolve(returnUrl, "/");

        if (!string.Equals(safeReturnUrl, "/", StringComparison.Ordinal))
        {
            properties.Items["returnUrl"] = safeReturnUrl;
        }

        try
        {
            // 直接执行挑战而不是返回 Results.Challenge：后者由框架在管道末端执行，
            // IdP 元数据拉不到时异常会一路冒到顶层变成 500 错误页，
            // 现场只看到「服务器错误」，与「IdP 没开」这个真实原因完全对不上。
            await httpContext.ChallengeAsync(provider.SchemeName, properties);
            return Results.Empty;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            loggerFactory.CreateLogger("OidcChallenge").LogWarning(
                exception, "发起统一登录失败：提供方 {Provider} 的元数据不可用。", provider.Code);

            if (httpContext.Response.HasStarted)
            {
                throw;
            }

            return Results.Redirect(
                $"/login?error={Uri.EscapeDataString("统一登录服务暂时不可用，请稍后重试或改用账号密码登录。")}");
        }
    }

    private static async Task<IResult> LoginAsync(
        HttpContext httpContext,
        PlatformAuthService authService,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.HasFormContentType)
        {
            return Results.BadRequest(new {message = "登录请求格式不正确。"});
        }

        IFormCollection form = await httpContext.Request.ReadFormAsync(cancellationToken);
        var userName = form["userName"].ToString();
        var password = form["password"].ToString();
        var returnUrl = form["returnUrl"].ToString();

        AuthSignInResult result = await authService.SignInAsync(userName, password, cancellationToken);

        if (!result.Succeeded || result.User is null)
        {
            var message = Uri.EscapeDataString(result.ErrorMessage ?? "登录失败。");
            var account = Uri.EscapeDataString(userName);
            return Results.Redirect($"/login?error={message}&userName={account}");
        }

        AuthUserDto user = result.User;

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            PlatformClaimsFactory.CreatePrincipal(user, DateTimeOffset.UtcNow, result.SecurityStamp),
            new AuthenticationProperties {IsPersistent = true});

        if (user.MustChangePassword)
        {
            return Results.Redirect("/change-password");
        }

        return Results.Redirect(PlatformReturnUrl.Resolve(
            returnUrl,
            await authService.ResolveLandingPageAsync(user, cancellationToken)));
    }

    private static async Task<IResult> LogoutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/login");
    }

    private static async Task<IResult> ChangePasswordAsync(
        HttpContext httpContext,
        PlatformAuthService authService,
        CancellationToken cancellationToken)
    {
        Guid? userId = ReadUserId(httpContext.User);

        if (userId is null)
        {
            return Results.Redirect("/login");
        }

        if (!httpContext.Request.HasFormContentType)
        {
            return Results.BadRequest(new {message = "改密请求格式不正确。"});
        }

        IFormCollection form = await httpContext.Request.ReadFormAsync(cancellationToken);
        var currentPassword = form["currentPassword"].ToString();
        var newPassword = form["newPassword"].ToString();
        var confirmPassword = form["confirmPassword"].ToString();

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            return Results.Redirect($"/change-password?error={Uri.EscapeDataString("两次输入的新密码不一致。")}");
        }

        AuthOperationResultDto result =
            await authService.ChangePasswordAsync(userId.Value, currentPassword, newPassword, cancellationToken);

        if (!result.Succeeded)
        {
            var message = Uri.EscapeDataString(result.ErrorMessage ?? "修改密码失败。");
            return Results.Redirect($"/change-password?error={message}");
        }

        // 改密后重新签发会话：旧会话里还带着 must_change_password，不清掉会被闸门反复拦回改密页。
        AuthUserDto? user = await authService.GetUserAsync(userId.Value, cancellationToken);

        if (user is null)
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            PlatformClaimsFactory.CreatePrincipal(
                user,
                DateTimeOffset.UtcNow,
                await authService.GetSecurityStampAsync(user.Id, cancellationToken)),
            new AuthenticationProperties {IsPersistent = true});

        return Results.Redirect(await authService.ResolveLandingPageAsync(user, cancellationToken));
    }

    private static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        PlatformAuthService authService,
        CancellationToken cancellationToken)
    {
        Guid? userId = ReadUserId(principal);

        // 车间只读终端没有用户标识，但它是一个有效会话：必须返回可识别的身份，
        // 否则 WASM 端拿到 401 会判为匿名并跳登录页，免登录看板在
        // 「第二次访问（WASM 缓存命中）」时就会整页失效。
        // Id 用 Guid.Empty 表示「不对应任何员工」：写操作的守卫据此拒绝归责到人。
        if (userId is null && PlatformClaimsFactory.IsBoardTerminal(principal))
        {
            return Results.Ok(new AuthUserDto(
                Guid.Empty,
                "车间只读终端",
                "车间只读终端",
                null,
                [],
                [AuthorizationCatalog.BanknotePaperDomain],
                AuthorizationCatalog.ViewAccessLevel,
                [],
                false,
                false,
                true));
        }

        if (userId is null)
        {
            return Results.Unauthorized();
        }

        AuthUserDto? user = await authService.GetUserAsync(userId.Value, cancellationToken);

        // 账号在使用过程中被停用：WASM 端据此退回匿名，下一次导航即被拦到登录页。
        return user is null ? Results.Unauthorized() : Results.Ok(user);
    }

    private static Guid? ReadUserId(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var raw = principal.FindFirst(AuthorizationCatalog.PlatformClaimTypes.UserId)?.Value;
        return Guid.TryParse(raw, out Guid userId) ? userId : null;
    }
}
