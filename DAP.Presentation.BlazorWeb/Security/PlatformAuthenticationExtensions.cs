using System.Security.Claims;
using DAP.Core.Shared.Catalog;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 注册认证与授权相关的服务，并提供「未登录一律拦到登录页」的请求闸门。
/// </summary>
public static class PlatformAuthenticationExtensions
{
    /// <summary>
    /// 会话 Cookie 名称。
    /// </summary>
    public const string CookieName = "dap_auth";

    /// <summary>
    /// 注册 Cookie 认证、授权与当前用户上下文。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">应用配置。</param>
    /// <returns>当前服务集合。</returns>
    public static IServiceCollection AddPlatformAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PlatformAuthOptions>(configuration.GetSection(PlatformAuthOptions.SectionName));

        CookieSessionOptions cookieOptions =
            configuration.GetSection($"{PlatformAuthOptions.SectionName}:Cookie").Get<CookieSessionOptions>()
            ?? new CookieSessionOptions();

        var slidingHours = Math.Max(1, cookieOptions.SlidingHours);
        var absoluteHours = Math.Max(slidingHours, cookieOptions.AbsoluteHours);

        services.AddCascadingAuthenticationState();
        services.AddAuthorization();
        services.AddScoped<PlatformAuthService>();

        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.LoginPath = "/login";
                options.LogoutPath = "/api/auth/logout";
                options.AccessDeniedPath = "/forbidden";
                options.ExpireTimeSpan = TimeSpan.FromHours(slidingHours);
                options.SlidingExpiration = true;

                options.Events = new CookieAuthenticationEvents
                {
                    // 绝对过期：Cookie 自身只做滑动续期，签发时间超过上限一律作废。
                    // 取 claim 里的签发时间而不是 Cookie 的 IssuedUtc——滑动续期会刷新后者，
                    // 用它判定等于永远不会到期。
                    OnValidatePrincipal = async context =>
                    {
                        var issuedAt = PlatformClaimsFactory.ReadIssuedAt(context.Principal);

                        if (issuedAt is null || DateTimeOffset.UtcNow - issuedAt.Value > TimeSpan.FromHours(absoluteHours))
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(
                                CookieAuthenticationDefaults.AuthenticationScheme);
                        }
                    },

                    // 接口不跳页面：返回 401，由调用方决定提示方式。
                    OnRedirectToLogin = context =>
                    {
                        if (IsApiRequest(context.Request))
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        }

                        context.Response.Redirect(context.RedirectUri);
                        return Task.CompletedTask;
                    },

                    OnRedirectToAccessDenied = context =>
                    {
                        if (IsApiRequest(context.Request))
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            return Task.CompletedTask;
                        }

                        context.Response.Redirect(context.RedirectUri);
                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }

    /// <summary>
    /// 在请求入口拦下未登录访问。页面请求跳登录页并带上回跳地址，接口请求返回 401。
    ///
    /// 这一层与路由守卫、端点策略并不重复：路由守卫在浏览器里跑（可绕过），
    /// 这里在服务端最先执行，保证未登录时连页面外壳都不会渲染。
    /// </summary>
    /// <param name="app">应用构建器。</param>
    /// <returns>当前应用构建器。</returns>
    public static IApplicationBuilder UsePlatformAuthenticationGate(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (IsExemptPath(context.Request.Path))
            {
                await next(context);
                return;
            }

            if (context.User.Identity?.IsAuthenticated != true)
            {
                await RejectAnonymousAsync(context);
                return;
            }

            // 首次登录强制改密：除改密页与认证接口外，一律先跳改密，避免「提示了但能绕过」。
            if (MustChangePassword(context.User) && !IsPasswordChangePath(context.Request.Path))
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new {message = "请先修改初始密码。"});
                    return;
                }

                context.Response.Redirect("/change-password");
                return;
            }

            await next(context);
        });
    }

    private static async Task RejectAnonymousAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new {message = "未登录或会话已过期，请重新登录。"});
            return;
        }

        var returnUrl = $"{context.Request.Path}{context.Request.QueryString}";
        context.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    private static bool MustChangePassword(ClaimsPrincipal principal)
    {
        return principal.HasClaim(AuthorizationCatalog.PlatformClaimTypes.MustChangePassword, "true");
    }

    private static bool IsPasswordChangePath(PathString path)
    {
        return path.StartsWithSegments("/change-password", StringComparison.OrdinalIgnoreCase)
               || path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断请求地址是否免登录：登录页本身、认证接口、健康检查、框架与静态资源。
    /// </summary>
    /// <param name="path">请求路径。</param>
    /// <returns>免登录时返回 true。</returns>
    private static bool IsExemptPath(PathString path)
    {
        if (!path.HasValue)
        {
            return false;
        }

        var value = path.Value!;

        // /_blazor、/_framework、/_content：Blazor 电路、WASM 运行时与静态资源包。
        if (value.StartsWith("/_", StringComparison.Ordinal))
        {
            return true;
        }

        // 带扩展名的请求一律放行，避免 CSS/JS/图标被 302 成 HTML。
        if (Path.HasExtension(value))
        {
            return true;
        }

        if (value.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return value.Equals("/login", StringComparison.OrdinalIgnoreCase)
               || value.Equals("/login/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsApiRequest(HttpRequest request)
    {
        return request.Path.StartsWithSegments("/api");
    }
}
