using DAP.Presentation.BlazorWeb.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DAP.Tests.UnitTests;

/// <summary>
/// <see cref="DAP.Presentation.BlazorWeb.Security.PlatformSameOriginGuard"/> 中间件级测试。
///
/// 既有 PlatformHardeningTests 覆盖了 RequiresCheck 分类与基本同源判定；
/// 这里补齐中间件管道行为（放行 / 403 JSON 拒绝）与 IsSameOrigin 未覆盖分支：
/// 端口不一致、Origin 非法格式。
/// </summary>
public class PlatformSameOriginGuardMiddlewareTests
{
    /// <summary>构建单段中间件管道并执行，返回处理后的上下文与末端标记。</summary>
    private static async Task<(HttpContext Context, bool EndpointReached)> InvokeAsync(
        Action<HttpContext> arrange)
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.ClearProviders());
        IServiceProvider provider = services.BuildServiceProvider();

        ApplicationBuilder app = new(provider);
        bool endpointReached = false;
        app.UsePlatformSameOriginGuard();
        app.Run(_ =>
        {
            endpointReached = true;
            return Task.CompletedTask;
        });
        RequestDelegate handler = app.Build();

        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();
        arrange(context);

        await handler(context);
        return (context, endpointReached);
    }

    [Fact]
    public async Task 跨源写请求_被拒绝并返回403与JSON说明()
    {
        (HttpContext context, bool endpointReached) = await InvokeAsync(context =>
        {
            context.Request.Path = "/api/auth/change-password";
            context.Request.Method = "POST";
            context.Request.Host = new HostString("dap.cdyc.local");
            context.Request.Headers.Origin = "http://evil.example.com";
        });

        Assert.False(endpointReached);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(context.Response.Body);
        string body = await reader.ReadToEndAsync();

        Assert.Contains("请求来源与本站不一致", body);
    }

    [Fact]
    public async Task 同源写请求_放行至后续管道()
    {
        (HttpContext context, bool endpointReached) = await InvokeAsync(context =>
        {
            context.Request.Path = "/api/auth/login";
            context.Request.Method = "POST";
            context.Request.Host = new HostString("dap.cdyc.local");
            context.Request.Headers.Origin = "http://dap.cdyc.local";
        });

        Assert.True(endpointReached);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task 只读请求与豁免路径_不做校验直接放行()
    {
        (HttpContext get, bool getReached) = await InvokeAsync(context =>
        {
            context.Request.Path = "/api/auth/me";
            context.Request.Method = "GET";
            context.Request.Host = new HostString("dap.cdyc.local");
            // GET 即使带上跨站 Origin 也放行：只读方法本就不改状态。
            context.Request.Headers.Origin = "http://evil.example.com";
        });

        (HttpContext exempt, bool exemptReached) = await InvokeAsync(context =>
        {
            // OIDC 回调是 IdP 跨站 POST 回来的，天然豁免。
            context.Request.Path = "/api/auth/oidc/corp/callback";
            context.Request.Method = "POST";
            context.Request.Host = new HostString("dap.cdyc.local");
            context.Request.Headers.Origin = "http://idp.example.com";
        });

        Assert.True(getReached);
        Assert.True(exemptReached);
    }

    [Fact]
    public void 同源判定_端口不一致应拒绝()
    {
        DefaultHttpContext context = new();
        context.Request.Host = new HostString("dap.cdyc.local", 5000);

        // Host 带端口而 Origin 是默认端口：不是同一来源。
        context.Request.Headers.Origin = "http://dap.cdyc.local";
        Assert.False(PlatformSameOriginGuard.IsSameOrigin(context.Request));

        context.Request.Headers.Origin = "http://dap.cdyc.local:6000";
        Assert.False(PlatformSameOriginGuard.IsSameOrigin(context.Request));

        context.Request.Headers.Origin = "http://dap.cdyc.local:5000";
        Assert.True(PlatformSameOriginGuard.IsSameOrigin(context.Request));
    }

    [Fact]
    public void 同源判定_非法URI格式的Origin应拒绝()
    {
        DefaultHttpContext context = new();
        context.Request.Host = new HostString("dap.cdyc.local");

        // Uri.TryCreate 失败按跨源处理，不能因为解析不了就放行。
        context.Request.Headers.Origin = "::::not-a-uri";
        Assert.False(PlatformSameOriginGuard.IsSameOrigin(context.Request));
    }

    [Fact]
    public void 同源判定_非法URI格式的Referer应拒绝()
    {
        DefaultHttpContext context = new();
        context.Request.Host = new HostString("dap.cdyc.local");

        context.Request.Headers.Referer = "::::not-a-uri";
        Assert.False(PlatformSameOriginGuard.IsSameOrigin(context.Request));
    }
}
