using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using Microsoft.AspNetCore.Components.Authorization;

namespace DAP.Presentation.BlazorWeb.Client.Services;

/// <summary>
/// WASM 端的认证状态来源。
///
/// 浏览器里没有 HttpContext，SSR 阶段的认证状态也带不过来，因此这里直接向 /api/auth/me 取一次：
/// 同源请求会自动带上 HttpOnly 会话 Cookie，取到的用户信息与授权视图与服务端完全同源。
/// </summary>
public sealed class PlatformAuthenticationStateProvider(HttpClient httpClient) : AuthenticationStateProvider
{
    private static readonly AuthenticationState AnonymousState =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private AuthenticationState? _cachedState;

    /// <inheritdoc />
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_cachedState is not null)
        {
            return _cachedState;
        }

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync("/api/auth/me");

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _cachedState = AnonymousState;
                return _cachedState;
            }

            if (!response.IsSuccessStatusCode)
            {
                // 网络或服务端异常不缓存：下次导航重试，避免把偶发失败当成「未登录」。
                return AnonymousState;
            }

            AuthUserDto? user = await response.Content.ReadFromJsonAsync<AuthUserDto>();

            if (user is null)
            {
                _cachedState = AnonymousState;
                return _cachedState;
            }

            _cachedState = new AuthenticationState(BuildPrincipal(user));
            return _cachedState;
        }
        catch (HttpRequestException)
        {
            return AnonymousState;
        }
        catch (TaskCanceledException)
        {
            return AnonymousState;
        }
    }

    /// <summary>
    /// 丢弃缓存并通知订阅者重新取状态。登录、登出、改密之后调用。
    /// </summary>
    public void Invalidate()
    {
        _cachedState = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private static ClaimsPrincipal BuildPrincipal(AuthUserDto user)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.Name, user.UserName),
            new(AuthorizationCatalog.PlatformClaimTypes.UserId, user.Id.ToString()),
            new(AuthorizationCatalog.PlatformClaimTypes.UserName, user.UserName),
            new(AuthorizationCatalog.PlatformClaimTypes.DisplayName, user.DisplayName),
            new(AuthorizationCatalog.PlatformClaimTypes.PageLevel, user.PageLevel.ToString())
        ];

        if (!string.IsNullOrEmpty(user.DepartmentName))
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.DepartmentName, user.DepartmentName));
        }

        if (user.IsSuperAdmin)
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.SuperAdmin, "true"));
        }

        // 车间只读终端：顶栏据此显示「只读」并隐藏登出按钮，
        // 与 SSR 阶段由 Cookie claims 渲染的结果保持一致。
        if (user.IsBoardTerminal)
        {
            claims.Add(new Claim(AuthorizationCatalog.PlatformClaimTypes.BoardTerminal, "true"));
        }

        claims.AddRange(user.Domains.Select(domain =>
            new Claim(AuthorizationCatalog.PlatformClaimTypes.Domain, domain)));

        claims.AddRange(user.Permissions.Select(permission =>
            new Claim(AuthorizationCatalog.PlatformClaimTypes.Permission, permission)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "dap-wasm"));
    }
}
