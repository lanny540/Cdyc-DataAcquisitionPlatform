using System.Security.Claims;
using DAP.Core.Shared.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace DAP.Presentation.BlazorWeb.Security.External;

/// <summary>
/// 把配置里的每家 OIDC 提供方注册成一个独立 scheme（docs/AUTH_DESIGN.md 11.2）。
///
/// 这里刻意复用 ASP.NET Core 的 OpenIdConnect 处理器而不是手写授权码流程：
/// state / nonce / PKCE / 元数据缓存 / id_token 签名校验这些安全细节由框架实现，
/// 手写一遍的收益为零，出错代价是登录被绕过。
/// </summary>
public static class OidcAuthenticationExtensions
{
    /// <summary>
    /// 注册全部已配置的 OIDC 提供方。
    /// </summary>
    /// <param name="builder">认证构建器。</param>
    /// <param name="options">OIDC 配置。</param>
    /// <returns>当前认证构建器。</returns>
    public static AuthenticationBuilder AddPlatformOidcProviders(
        this AuthenticationBuilder builder,
        OidcOptions options)
    {
        foreach (OidcProviderOptions provider in options.EnabledProviders())
        {
            builder.AddOpenIdConnect(provider.SchemeName, openIdOptions => Configure(openIdOptions, provider));
        }

        return builder;
    }

    private static void Configure(OpenIdConnectOptions options, OidcProviderOptions provider)
    {
        options.Authority = provider.Authority;
        options.ClientId = provider.ClientId;
        options.ClientSecret = string.IsNullOrWhiteSpace(provider.ClientSecret) ? null : provider.ClientSecret;
        options.RequireHttpsMetadata = provider.RequireHttpsMetadata;

        // Authorization Code + PKCE：code 换 token 在服务端完成，
        // 浏览器只拿到一次性 code，外部令牌一个都不落到前端。
        options.ResponseType = "code";
        options.UsePkce = true;

        // 回调必须落在 /api/auth 下：登录闸门对这一段是放行的，
        // 否则 IdP 带 code 回跳时会被当成未登录请求 302 回登录页。
        options.CallbackPath = provider.CallbackPath;
        options.SignedOutCallbackPath = $"/api/auth/oidc/{provider.Code}/signed-out";

        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.MapInboundClaims = false;

        options.Scope.Clear();

        foreach (var scope in provider.Scopes)
        {
            options.Scope.Add(scope);
        }

        options.TokenValidationParameters.NameClaimType = provider.NameClaim;
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = context => HandleTokenValidatedAsync(context, provider),

            // 用户取消授权、state 不匹配、IdP 报错都会走到这里。对外只给一句中文，
            // 具体原因由处理器的日志记录，不把英文错误码甩到登录页上。
            OnRemoteFailure = context =>
            {
                context.Response.Redirect(
                    $"/login?error={Uri.EscapeDataString("统一登录未完成，请重试或改用账号密码登录。")}");
                context.HandleResponse();
                return Task.CompletedTask;
            }
        };
    }

    private static async Task HandleTokenValidatedAsync(
        TokenValidatedContext context,
        OidcProviderOptions provider)
    {
        IEnumerable<Claim> claims = context.Principal?.Claims ?? [];
        OidcUserProfile? profile = OidcClaimsMapper.Map(claims, provider);

        if (profile is null)
        {
            await FailAsync(context, "统一登录返回的身份信息不完整（缺少 sub），请联系管理员。");
            return;
        }

        IServiceProvider services = context.HttpContext.RequestServices;
        CancellationToken cancellationToken = context.HttpContext.RequestAborted;

        OidcProvisionResult provision = await services
            .GetRequiredService<OidcSignInService>()
            .ProvisionAsync(provider, profile, cancellationToken);

        if (!provision.Succeeded || provision.UserId is null)
        {
            await FailAsync(context, provision.ErrorMessage ?? "统一登录失败，请联系管理员。");
            return;
        }

        PlatformAuthService authService = services.GetRequiredService<PlatformAuthService>();
        AuthUserDto? user = await authService.GetUserAsync(provision.UserId.Value, cancellationToken);

        if (user is null)
        {
            await FailAsync(context, "该账号已停用，请联系管理员。");
            return;
        }

        await context.HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            PlatformClaimsFactory.CreatePrincipal(user, DateTimeOffset.UtcNow),
            new AuthenticationProperties {IsPersistent = true});

        // 统一登录不走「先改密」分支：外部 IdP 已经完成了身份校验，
        // 再强制改一个平台本地密码没有意义（域账号本来也没有本地密码）。
        var returnUrl = context.Properties?.Items.TryGetValue("returnUrl", out var value) == true
            ? value
            : null;

        var landingPage = await authService.ResolveLandingPageAsync(user, cancellationToken);

        context.Response.Redirect(PlatformReturnUrl.Resolve(returnUrl, landingPage));
        context.HandleResponse();
    }

    private static Task FailAsync(TokenValidatedContext context, string message)
    {
        context.Response.Redirect($"/login?error={Uri.EscapeDataString(message)}");
        context.HandleResponse();
        return Task.CompletedTask;
    }
}
