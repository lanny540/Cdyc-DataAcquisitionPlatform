using Microsoft.AspNetCore.HttpOverrides;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 反向代理下的客户端地址与协议还原。
///
/// 为什么必须显式配置：车间终端的 IP 白名单与登录限流都按客户端 IP 分区，
/// 一旦前面加了 Nginx / F5 而没有 <c>ForwardedHeaders</c>，所有请求的 RemoteIpAddress
/// 都会是代理地址——白名单失效（谁都能拿到只读会话）或全站共用一个限流桶。
/// 反过来，无条件信任 <c>X-Forwarded-For</c> 同样危险：任何人自带一个伪造头就能冒充白名单 IP。
/// 因此只在「显式启用 + 显式列出代理地址」时才还原，且只信任这些代理。
/// </summary>
public static class PlatformTransportExtensions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "Auth:ForwardedHeaders";

    /// <summary>
    /// 按配置启用转发头还原。未启用或未配置代理地址时不做任何事（保持直连语义）。
    /// </summary>
    /// <param name="app">应用构建器。</param>
    /// <param name="configuration">应用配置。</param>
    /// <returns>当前应用构建器。</returns>
    public static IApplicationBuilder UsePlatformForwardedHeaders(
        this IApplicationBuilder app,
        IConfiguration configuration)
    {
        ForwardedHeadersSettings settings =
            configuration.GetSection(SectionName).Get<ForwardedHeadersSettings>()
            ?? new ForwardedHeadersSettings();

        if (!settings.Enabled || settings.KnownProxies.Length == 0)
        {
            return app;
        }

        ForwardedHeadersOptions options = new()
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = Math.Max(1, settings.ForwardLimit)
        };

        // 先清空默认信任（含 loopback 与私有网段），只保留配置里点名的代理：
        // 默认值在「代理与本机同机」时方便，但会让同机的任何进程都能伪造来源。
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        foreach (var entry in settings.KnownProxies)
        {
            if (System.Net.IPAddress.TryParse(entry, out System.Net.IPAddress? address))
            {
                options.KnownProxies.Add(address);
            }
        }

        return app.UseForwardedHeaders(options);
    }

    /// <summary>
    /// 收集传输层的启动提示。这些不算错误，但必须在日志里说清楚，
    /// 否则现象会表现为「登录后又跳回登录页」「白名单不生效」，排查方向完全错。
    /// </summary>
    /// <param name="configuration">应用配置。</param>
    /// <param name="environment">宿主环境。</param>
    /// <returns>提示列表（级别, 文案）。</returns>
    public static IReadOnlyList<(LogLevel Level, string Message)> DescribeTransportWarnings(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        List<(LogLevel, string)> warnings = [];

        CookieSessionOptions cookieOptions =
            configuration.GetSection($"{PlatformAuthOptions.SectionName}:Cookie").Get<CookieSessionOptions>()
            ?? new CookieSessionOptions();

        if (!environment.IsDevelopment())
        {
            if (cookieOptions.RequireHttps)
            {
                warnings.Add((LogLevel.Information,
                    "会话 Cookie 已强制 Secure：请确认前端通过 HTTPS 访问（可由 Kestrel 或反向代理终止 TLS）。"
                    + "若直接以明文 HTTP 访问，浏览器不会回传会话 Cookie，表现为「登录成功后又回到登录页」。"));
            }
            else
            {
                warnings.Add((LogLevel.Warning,
                    "已关闭 HTTPS 强制（Auth:Cookie:RequireHttps=false）：会话 Cookie 将随明文 HTTP 传输，"
                    + "任何能抓包的人都能直接复用会话。仅在物理隔离的内网短期使用。"));
            }
        }

        ForwardedHeadersSettings forwardedHeaders =
            configuration.GetSection(SectionName).Get<ForwardedHeadersSettings>()
            ?? new ForwardedHeadersSettings();

        if (forwardedHeaders.Enabled && forwardedHeaders.KnownProxies.Length == 0)
        {
            warnings.Add((LogLevel.Warning,
                $"{SectionName}:Enabled 为 true 但没有配置 KnownProxies，转发头还原已停用。"
                + "此时所有请求的客户端地址都是代理地址，车间 IP 白名单与登录限流会失效。"));
        }

        return warnings;
    }
}

/// <summary>
/// 转发头配置。
/// </summary>
public sealed class ForwardedHeadersSettings
{
    /// <summary>
    /// 获取或设置是否启用转发头还原。默认关闭（直连 Kestrel 的场景不需要）。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 获取或设置受信代理地址列表。只有来自这些地址的转发头会被采纳。
    /// </summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>
    /// 获取或设置信任的转发层数。通常为 1（一层反向代理）。
    /// </summary>
    public int ForwardLimit { get; set; } = 1;
}
