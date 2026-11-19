using DAP.Presentation.BlazorWeb.Security;
using DAP.Presentation.BlazorWeb.Security.Auditing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DAP.Tests.UnitTests;

/// <summary>
/// <see cref="DAP.Presentation.BlazorWeb.Security.ForbiddenAccessExtensions"/> 越权异常处理中间件测试。
///
/// 领域服务装饰器抛出的 ForbiddenAccessException 统一映射为 API 403（JSON）
/// 或页面跳转 /forbidden；电路帧已开始下发时只能终止连接（重抛）。
/// </summary>
public class ForbiddenAccessExtensionsTests
{
    /// <summary>构建含越权处理中间件的管道，末端行为由 next 控制。</summary>
    private static async Task<HttpContext> InvokeAsync(
        Func<HttpContext, Task> next,
        string path = "/api/shift-schedules/import")
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.ClearProviders());
        IServiceProvider provider = services.BuildServiceProvider();

        ApplicationBuilder app = new(provider);
        app.UseForbiddenAccessHandling();
        app.Run(context => next(context));
        RequestDelegate handler = app.Build();

        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();
        context.Request.Path = path;
        context.Request.Method = "POST";

        await handler(context);
        return context;
    }

    [Fact]
    public async Task API路径抛越权异常_返回403与JSON消息()
    {
        HttpContext context = await InvokeAsync(_ =>
            Task.FromException(new ForbiddenAccessException("当前账号没有执行该操作的权限，请联系管理员。")));

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(context.Response.Body);
        string body = await reader.ReadToEndAsync();

        Assert.Contains("没有执行该操作的权限", body);
    }

    [Fact]
    public async Task 页面路径抛越权异常_跳转到forbidden页()
    {
        HttpContext context = await InvokeAsync(_ =>
            Task.FromException(new ForbiddenAccessException("越权")), path: "/shift-schedules");

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/forbidden", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task 无异常_请求正常通过()
    {
        HttpContext context = await InvokeAsync(http =>
        {
            http.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task 其他异常_不被中间件吞掉_原样上抛()
    {
        InvalidOperationException thrown = new("业务异常");

        InvalidOperationException caught = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeAsync(_ => Task.FromException(thrown)));

        Assert.Same(thrown, caught);
    }

    /// <summary>固定为「已开始下发」的响应特性，用于触发中间件的重抛分支。</summary>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted => true;

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public string? ReasonPhrase { get; set; }

        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }
    }

    [Fact]
    public async Task 响应已开始下发_无法改写_重抛异常()
    {
        // 电路帧已经开始下发：改写响应会破坏已发出的字节，只能终止连接。
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => InvokeAsync(http =>
        {
            http.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
            return Task.FromException(new ForbiddenAccessException("会话已失效"));
        }));
    }
}
