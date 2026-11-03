using System.Security.Claims;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

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
        group.MapPost("/login", (Delegate)LoginAsync)
            .WithName("AuthLogin")
            .DisableAntiforgery();

        group.MapPost("/logout", (Delegate)LogoutAsync)
            .WithName("AuthLogout")
            .DisableAntiforgery();

        group.MapPost("/change-password", (Delegate)ChangePasswordAsync)
            .WithName("AuthChangePassword")
            .DisableAntiforgery();

        group.MapGet("/me", GetCurrentUserAsync)
            .WithName("AuthMe");

        return endpoints;
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
            PlatformClaimsFactory.CreatePrincipal(user, DateTimeOffset.UtcNow),
            new AuthenticationProperties {IsPersistent = true});

        if (user.MustChangePassword)
        {
            return Results.Redirect("/change-password");
        }

        return Results.Redirect(ResolveTarget(returnUrl, await authService.ResolveLandingPageAsync(user, cancellationToken)));
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
            PlatformClaimsFactory.CreatePrincipal(user, DateTimeOffset.UtcNow),
            new AuthenticationProperties {IsPersistent = true});

        return Results.Redirect(await authService.ResolveLandingPageAsync(user, cancellationToken));
    }

    private static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        PlatformAuthService authService,
        CancellationToken cancellationToken)
    {
        Guid? userId = ReadUserId(principal);

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

    /// <summary>
    /// 计算登录后的跳转目标：优先回跳原始地址，但只接受站内相对地址，避免被用作跳板。
    /// </summary>
    private static string ResolveTarget(string? returnUrl, string landingPage)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return landingPage;
        }

        var candidate = returnUrl.Trim();

        if (!candidate.StartsWith('/') || candidate.StartsWith("//", StringComparison.Ordinal)
            || candidate.StartsWith("/\\", StringComparison.Ordinal))
        {
            return landingPage;
        }

        // 登录页与认证接口自身不能作为回跳目标，否则会形成重定向环。
        if (candidate.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
        {
            return landingPage;
        }

        return candidate;
    }
}
