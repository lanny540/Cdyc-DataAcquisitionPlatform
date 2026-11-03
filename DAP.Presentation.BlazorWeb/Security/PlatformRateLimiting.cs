using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 认证端点的请求限流。
///
/// 已有的「连续 5 次失败锁定账号」只挡单账号爆破，挡不住两件事：
/// 一是密码喷洒（一个弱口令去撞成千上万个账号，每个账号只错一次，永远不触发锁定）；
/// 二是计算型 DoS——每次登录都要跑一遍 PBKDF2（21 万次迭代，几十毫秒 CPU），
/// 几百个并发请求就能把站点拖慢，而攻击者不需要知道任何账号。
///
/// 只对「会消耗凭据校验」的端点限流：<c>/api/auth/me</c> 每个页面加载都会调用，
/// 把它算进去会让正常用户被自己的刷新频率卡住。
/// </summary>
public static class PlatformRateLimiting
{
    /// <summary>
    /// 登录与改密共用的限流策略名。
    /// </summary>
    public const string CredentialPolicy = "auth-credential";

    /// <summary>
    /// 每分钟每 IP 允许的凭据校验次数。
    ///
    /// 取 20 而不是 5：办公区通常几十人共用同一个出口 IP，按人限制会被 NAT 放大成误伤。
    /// 单账号的暴力破解仍由账号锁定策略兜住，这一层的目标是「批量喷洒」与「算力消耗」。
    /// </summary>
    public const int PermitLimitPerMinute = 20;

    /// <summary>
    /// 注册限流策略。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>当前服务集合。</returns>
    public static IServiceCollection AddPlatformRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(CredentialPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    // 直连 Kestrel 时 RemoteIpAddress 即真实对端；
                    // 加反向代理后必须配 ForwardedHeaders 且只信已知代理，否则所有人共用一个桶。
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PermitLimitPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new {message = "登录尝试过于频繁，请稍后再试。"},
                    cancellationToken);
            };
        });

        return services;
    }
}
