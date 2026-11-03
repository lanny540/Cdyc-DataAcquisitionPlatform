using System.Globalization;
using System.Net;
using System.Security.Claims;
using DAP.Core.Shared.Catalog;
using DAP.Presentation.BlazorWeb.Client.Layout;
using DAP.Presentation.BlazorWeb.Security.Directory;
using DAP.Presentation.BlazorWeb.Security.External;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

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

        BoardOptions boardOptions =
            configuration.GetSection($"{PlatformAuthOptions.SectionName}:Board").Get<BoardOptions>()
            ?? new BoardOptions();

        services.AddCascadingAuthenticationState();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PlatformAuthorizationPolicyProvider>();
        services.AddScoped<PlatformAuthService>();
        services.AddScoped<PageAccessResolver>();

        // 外部身份源（docs/AUTH_DESIGN.md 11）：LDAP 与 OIDC 都默认关闭，
        // 关闭时这些服务仍可解析，只是不产生任何外部调用。
        services.AddScoped<ILdapDirectory, LdapDirectoryClient>();
        services.AddScoped<DirectoryAccountService>();
        services.AddScoped<DirectorySyncService>();
        services.AddScoped<DepartmentCodeResolver>();
        services.AddScoped<OidcSignInService>();
        services.AddHostedService<DirectorySyncScheduler>();

        AuthenticationBuilder authenticationBuilder = services
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

                        // 车间只读会话（docs/AUTH_DESIGN.md 6.6 硬约束 5）：
                        // 绑定签发 IP，不一致立即作废——Cookie 被复制到别的机器也用不了；
                        // 固定有效期不滑动：滑动续期会让被盗 Cookie 无限延长，
                        // 固定到期后由白名单 IP 重新签发，现场无感。
                        if (PlatformClaimsFactory.IsBoardTerminal(context.Principal))
                        {
                            var issuedIp = context.Principal?.FindFirst(
                                AuthorizationCatalog.PlatformClaimTypes.BoardIssuedIp)?.Value;
                            var remoteIp = context.HttpContext.Connection.RemoteIpAddress?.ToString();

                            var boardExpired = issuedAt is null
                                               || DateTimeOffset.UtcNow - issuedAt.Value
                                               > TimeSpan.FromHours(Math.Max(1, boardOptions.SessionHours));

                            if (!string.Equals(issuedIp, remoteIp, StringComparison.Ordinal) || boardExpired)
                            {
                                context.RejectPrincipal();
                                await context.HttpContext.SignOutAsync(
                                    CookieAuthenticationDefaults.AuthenticationScheme);
                            }

                            return;
                        }

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

        // 每家 OIDC 提供方一个 scheme，全部来自配置；新增一家只加一段配置。
        OidcOptions oidcOptions =
            configuration.GetSection($"{PlatformAuthOptions.SectionName}:Oidc").Get<OidcOptions>()
            ?? new OidcOptions();

        authenticationBuilder.AddPlatformOidcProviders(oidcOptions);

        return services;
    }

    /// <summary>
    /// 在请求入口拦下未登录访问。页面请求跳登录页并带上回跳地址，接口请求返回 401。
    ///
    /// 这一层与路由守卫、端点策略并不重复：路由守卫在浏览器里跑（可绕过），
    /// 这里在服务端最先执行，保证未登录时连页面外壳都不会渲染。
    ///
    /// 登录之后另有一道页面归属判定（7.3）：按 domain_pages 解析当前路径的域与等级，
    /// 域不可见或等级不足一律 302 /forbidden——否则普通用户直接输入配置页地址
    /// 会看到一个能打开的页面，只是数据接口 403。
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
                // 车间只读终端：命中 IP 白名单的未认证请求签发只读会话（docs/AUTH_DESIGN.md 6.6）。
                // IP 只决定「是否签发」，之后所有请求与 Blazor 电路一律基于该 Cookie 的 claims 判定。
                if (await TryIssueBoardSessionAsync(app, context))
                {
                    await next(context);
                    return;
                }

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

            // 页面归属判定：只判页面请求，API 由端点策略拦截（域 + 等级 + 权限点）。
            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                var denied = await IsPageAccessDeniedAsync(app, context);

                if (denied)
                {
                    context.Response.Redirect("/forbidden");
                    return;
                }
            }

            await next(context);
        });
    }

    /// <summary>
    /// 判定当前页面请求是否被域与等级拦下。路径未登记（如 /forbidden、/no-access）不判。
    /// 车间只读终端（等级 1）与普通用户在配置类页面（等级 2）上得到相同结果：被拦。
    /// 跨域共享页（如用户与角色）不判域授权，改为判权限点——部门管理员的可见域是业务域，
    /// 但用户管理是他们职责内的事，靠 action.user.manage 放行而非把系统域授予他们。
    /// </summary>
    private static async Task<bool> IsPageAccessDeniedAsync(IApplicationBuilder app, HttpContext context)
    {
        NavItem? sharedPage = NavigationCatalog.ResolveSharedPage(context.Request.Path);

        if (sharedPage is not null)
        {
            return (sharedPage.RequiresPermission
                    && !PlatformClaimsFactory.HasPermission(context.User, sharedPage.RequiredPermission!))
                   || PlatformClaimsFactory.ReadPageLevel(context.User) < sharedPage.AccessLevel;
        }

        // 中间件无法构造注入，从根容器开 scope 取解析器；判定走 60 秒缓存，开销可控。
        using var scope = app.ApplicationServices.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<PageAccessResolver>();

        PageAccessRequirement? requirement = await resolver.ResolveAsync(
            context.Request.Path.Value ?? string.Empty,
            context.RequestAborted);

        if (requirement is null)
        {
            return false;
        }

        return !PlatformClaimsFactory.CanAccessDomain(context.User, requirement.DomainCode)
               || PlatformClaimsFactory.ReadPageLevel(context.User) < requirement.AccessLevel;
    }

    /// <summary>
    /// 尝试为车间只读终端签发免登录会话。claims 只有三个：
    /// 车间终端标记 + 钞纸域 + 页面等级 1——「数据指标改 IP 白名单」在现有模型里
    /// 就等价于「钞纸域 + 等级 1」，不需要新增任何权限概念（docs/AUTH_DESIGN.md 6.6）。
    /// 每次签发写一条应用日志，用于追溯这个免登录入口最近被谁用过。
    /// </summary>
    /// <returns>签发成功时 true，请求以只读会话继续。</returns>
    private static async Task<bool> TryIssueBoardSessionAsync(IApplicationBuilder app, HttpContext context)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<PlatformAuthOptions>>().Value.Board;

        if (!options.Enabled || options.IpWhitelist.Length == 0)
        {
            return false;
        }

        IPAddress? remoteIp = context.Connection.RemoteIpAddress;

        if (remoteIp is null)
        {
            return false;
        }

        if (!options.IpWhitelist.Any(entry => BoardIpWhitelist.IsMatch(remoteIp, entry)))
        {
            return false;
        }

        // 直连 Kestrel 时 RemoteIpAddress 即真实对端地址。一旦前面加了反向代理，
        // 必须配置 ForwardedHeaders 且只信已知代理，否则伪造请求头即可获得只读会话。
        DateTimeOffset now = DateTimeOffset.UtcNow;

        ClaimsIdentity identity = new(
        [
            new Claim(ClaimTypes.Name, "车间只读终端"),
            new Claim(AuthorizationCatalog.PlatformClaimTypes.BoardTerminal, "true"),
            new Claim(AuthorizationCatalog.PlatformClaimTypes.BoardIssuedIp, remoteIp.ToString()),
            new Claim(AuthorizationCatalog.PlatformClaimTypes.DisplayName, "车间只读终端"),
            new Claim(AuthorizationCatalog.PlatformClaimTypes.PageLevel,
                AuthorizationCatalog.ViewAccessLevel.ToString(CultureInfo.InvariantCulture)),
            new Claim(AuthorizationCatalog.PlatformClaimTypes.Domain, AuthorizationCatalog.BanknotePaperDomain),
            new Claim(AuthorizationCatalog.PlatformClaimTypes.IssuedAt,
                now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = new ClaimsPrincipal(identity);

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                // 固定有效期，覆盖一个班次；到期后由白名单 IP 重新签发，现场无感。
                ExpiresUtc = now.AddHours(Math.Max(1, options.SessionHours))
            });

        context.User = principal;

        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("BoardTerminal");
        logger.LogInformation(
            "车间只读终端会话签发：IP={Ip}，User-Agent={UserAgent}，有效期 {Hours} 小时（固定，不滑动）。",
            remoteIp,
            context.Request.Headers.UserAgent.ToString(),
            options.SessionHours);

        return true;
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
