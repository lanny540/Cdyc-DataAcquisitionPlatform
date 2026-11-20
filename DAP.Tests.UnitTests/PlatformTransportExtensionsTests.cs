using System.Net;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DAP.Tests.UnitTests;

/// <summary>
/// <see cref="DAP.Presentation.BlazorWeb.Security.PlatformTransportExtensions"/> 转发头与启动提示测试。
///
/// 车间终端 IP 白名单与登录限流都按客户端 IP 分区：加反向代理后不配
/// ForwardedHeaders 会让所有请求都是代理地址。这里验证「显式启用 + 显式列出
/// 代理地址」才还原，以及 DescribeTransportWarnings 的各条提示分支。
/// </summary>
public class PlatformTransportExtensionsTests
{
    /// <summary>最小 Web 宿主环境假件。</summary>
    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "DAP.Tests.UnitTests";

        public string EnvironmentName { get; set; } = Environments.Production;

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>按配置构建转发头管道并执行一次请求。</summary>
    private static async Task<HttpContext> InvokeAsync(IConfiguration configuration, IPAddress remoteIp)
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.ClearProviders());
        IServiceProvider provider = services.BuildServiceProvider();

        ApplicationBuilder app = new(provider);
        app.UsePlatformForwardedHeaders(configuration);
        app.Run(_ => Task.CompletedTask);
        RequestDelegate handler = app.Build();

        DefaultHttpContext context = new();
        context.Request.Method = "GET";
        context.Connection.RemoteIpAddress = remoteIp;
        context.Request.Headers["X-Forwarded-For"] = "192.168.1.100";
        await handler(context);

        return context;
    }

    [Fact]
    public async Task 启用且配置已知代理_客户端地址按转发头还原()
    {
        IConfiguration configuration = HostingFakes.CreateConfiguration(
            ("Auth:ForwardedHeaders:Enabled", "true"),
            ("Auth:ForwardedHeaders:KnownProxies:0", "10.0.0.9"));

        HttpContext context = await InvokeAsync(configuration, IPAddress.Parse("10.0.0.9"));

        // 代理在受信列表内：X-Forwarded-For 被采纳，白名单按真实客户端地址判定。
        Assert.Equal(IPAddress.Parse("192.168.1.100"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task 未启用_转发头被忽略_保持直连语义()
    {
        IConfiguration configuration = HostingFakes.CreateConfiguration();

        HttpContext context = await InvokeAsync(configuration, IPAddress.Parse("10.0.0.9"));

        Assert.Equal(IPAddress.Parse("10.0.0.9"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task 启用但未配置代理_转发头不生效()
    {
        IConfiguration configuration = HostingFakes.CreateConfiguration(
            ("Auth:ForwardedHeaders:Enabled", "true"));

        HttpContext context = await InvokeAsync(configuration, IPAddress.Parse("10.0.0.9"));

        // 信任一个空代理列表等于直连：自带伪造 X-Forwarded-For 冒充白名单的请求不被采纳。
        Assert.Equal(IPAddress.Parse("10.0.0.9"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task 来源不在受信代理列表_转发头被忽略()
    {
        IConfiguration configuration = HostingFakes.CreateConfiguration(
            ("Auth:ForwardedHeaders:Enabled", "true"),
            ("Auth:ForwardedHeaders:KnownProxies:0", "10.0.0.9"));

        // 请求直接来自未知地址：头里声称的客户端地址不可信。
        HttpContext context = await InvokeAsync(configuration, IPAddress.Parse("172.16.0.1"));

        Assert.Equal(IPAddress.Parse("172.16.0.1"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public void 生产环境_强制HTTPS_提示为信息级()
    {
        IConfiguration configuration = HostingFakes.CreateConfiguration(
            ("Auth:Cookie:RequireHttps", "true"));

        IReadOnlyList<(LogLevel Level, string Message)> warnings =
            PlatformTransportExtensions.DescribeTransportWarnings(
                configuration, new TestWebHostEnvironment());

        (LogLevel level, string message) = Assert.Single(warnings);
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains("Secure", message);
    }

    [Fact]
    public void 生产环境_关闭HTTPS强制_提示为警告级()
    {
        IConfiguration configuration = HostingFakes.CreateConfiguration(
            ("Auth:Cookie:RequireHttps", "false"));

        IReadOnlyList<(LogLevel Level, string Message)> warnings =
            PlatformTransportExtensions.DescribeTransportWarnings(
                configuration, new TestWebHostEnvironment());

        (LogLevel level, string message) = Assert.Single(warnings);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("RequireHttps=false", message);
    }

    [Fact]
    public void 开发环境_不输出Cookie提示()
    {
        IConfiguration configuration = HostingFakes.CreateConfiguration(
            ("Auth:Cookie:RequireHttps", "false"));

        TestWebHostEnvironment environment = new() { EnvironmentName = Environments.Development };

        Assert.Empty(
            PlatformTransportExtensions.DescribeTransportWarnings(
                configuration, environment));
    }

    [Fact]
    public void 启用转发头但无代理列表_输出警告()
    {
        IConfiguration configuration = HostingFakes.CreateConfiguration(
            ("Auth:ForwardedHeaders:Enabled", "true"));

        // 开发环境跳过 Cookie 提示，隔离出转发头这一条警告。
        TestWebHostEnvironment environment = new() { EnvironmentName = Environments.Development };

        IReadOnlyList<(LogLevel Level, string Message)> warnings =
            PlatformTransportExtensions.DescribeTransportWarnings(
                configuration, environment);

        (LogLevel level, string message) = Assert.Single(warnings);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("KnownProxies", message);
        Assert.Contains("白名单", message);
    }
}
