using DAP.Presentation.BlazorWeb.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DAP.Tests.UnitTests;

/// <summary>
/// <see cref="DAP.Presentation.BlazorWeb.Security.PlatformRateLimiting"/> 限流策略测试。
///
/// 覆盖两块可测规则：注册结果（429 拒绝码 + 策略已登记）与 OnRejected 拒绝回调——
/// 表单请求 303 跳回对应页面并带中文原因，非表单请求回 JSON。
/// </summary>
public class PlatformRateLimitingTests
{
    private static RateLimiterOptions BuildOptions()
    {
        ServiceCollection services = new();
        services.AddPlatformRateLimiting();
        using ServiceProvider provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;
    }

    private static async Task<HttpContext> RejectAsync(
        string path, string? contentType)
    {
        RateLimiterOptions options = BuildOptions();
        Assert.NotNull(options.OnRejected);

        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();
        context.Request.Path = path;
        context.Request.Method = "POST";
        if (contentType is not null)
        {
            context.Request.ContentType = contentType;
        }

        OnRejectedContext rejected = new() { HttpContext = context, Lease = null! };
        await options.OnRejected(rejected, CancellationToken.None);

        return context;
    }

    [Fact]
    public void 注册结果_拒绝状态码为429()
    {
        RateLimiterOptions options = BuildOptions();

        Assert.Equal(StatusCodes.Status429TooManyRequests, options.RejectionStatusCode);
    }

    [Fact]
    public void 注册结果_拒绝回调已配置()
    {
        RateLimiterOptions options = BuildOptions();

        // 表单拒绝逻辑（303 跳回登录/改密页）挂在该回调上，缺失会让用户看到一坨 JSON。
        Assert.NotNull(options.OnRejected);
    }

    [Fact]
    public async Task 拒绝回调_表单改密请求_303跳回改密页并带原因()
    {
        HttpContext context = await RejectAsync(
            "/api/auth/change-password", "application/x-www-form-urlencoded");

        Assert.Equal(StatusCodes.Status303SeeOther, context.Response.StatusCode);
        string location = context.Response.Headers.Location.ToString();

        Assert.StartsWith("/change-password?error=", location);
        Assert.Contains("操作过于频繁", Uri.UnescapeDataString(location));
    }

    [Fact]
    public async Task 拒绝回调_表单登录请求_303跳回登录页()
    {
        HttpContext context = await RejectAsync(
            "/api/auth/login", "application/x-www-form-urlencoded");

        Assert.Equal(StatusCodes.Status303SeeOther, context.Response.StatusCode);
        string location = context.Response.Headers.Location.ToString();

        Assert.StartsWith("/login?error=", location);
        Assert.Contains("操作过于频繁", Uri.UnescapeDataString(location));
    }

    [Fact]
    public async Task 拒绝回调_非表单请求_返回429与JSON()
    {
        // fetch/API 客户端不走表单：直接 429 + JSON，浏览器原生表单才 303。
        HttpContext context = await RejectAsync("/api/auth/login", "application/json");

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(context.Response.Body);
        string body = await reader.ReadToEndAsync();

        Assert.Contains("登录尝试过于频繁", body);
    }

    [Fact]
    public void 常量_限流阈值取20_避免办公区NAT误伤()
    {
        // 取 20 而不是 5：几十人共用出口 IP，按人限制会被 NAT 放大成误伤。
        Assert.Equal(20, PlatformRateLimiting.PermitLimitPerMinute);
    }
}
