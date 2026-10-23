using System.Net;
using System.Text;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 验证采集服务器探测服务的返回值解析逻辑。
/// </summary>
public sealed class AcquisitionServerProbeServiceTests
{
    private const string HistorianCurrentValuePath = "/historian-rest-api/v1/datapoints/currentvalue";

    [Fact]
    public async Task ProbeAsync_MockResponse_ReturnsParsedValue()
    {
        AcquisitionServerProbeService service = CreateService();

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.Historian,
            "http://10.9.3.54:8080",
            new Dictionary<string, string> {["useMockResponses"] = "true"},
            "tag001");

        Assert.True(result.Success);
        Assert.Equal("接口读取", result.ProbeKind);
        Assert.Equal("12.34", result.ParsedValue);
    }

    [Fact]
    public async Task ProbeAsync_BlankAddress_ReturnsFailure()
    {
        AcquisitionServerProbeService service = CreateService();

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.Historian,
            "   ",
            new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Contains("不能为空", result.ErrorMessage);
    }

    [Fact]
    public async Task ProbeAsync_UnknownType_ReturnsFailure()
    {
        AcquisitionServerProbeService service = CreateService();

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            "Unsupported",
            "http://10.0.0.1:8080",
            new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Contains("不支持的采集服务器类型", result.ErrorMessage);
    }

    [Fact]
    public async Task ProbeAsync_ExtractsValueMatchingTagNameFromArray()
    {
        // Historian 最新值返回数组，需要按 tagName 匹配当前采集点位。
        const string payload = """
                               [{"tagName":"other","value":1.1},{"tagName":"tag001","value":23.456}]
                               """;

        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, payload);
        AcquisitionServerProbeService service = CreateService(handler);

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.Historian,
            "http://10.9.3.54:8080",
            new Dictionary<string, string>
            {
                ["clientId"] = "admin",
                ["clientSecret"] = "Pa55word"
            },
            "tag001");

        Assert.True(result.Success);
        Assert.Equal("23.456", result.ParsedValue);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public async Task ProbeAsync_UsesConfiguredCurrentValuePath()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """[{"tagName":"t","value":7}]""");
        AcquisitionServerProbeService service = CreateService(handler);

        await service.ProbeAsync(
            AcquisitionServerCatalogKeys.Historian,
            "http://10.9.3.54:8080",
            new Dictionary<string, string>
            {
                ["clientId"] = "admin",
                ["clientSecret"] = "Pa55word",
                ["currentValuePath"] = "/custom/current"
            },
            "t");

        // 服务器上配置的接口路径应覆盖默认值。
        Assert.Contains("/custom/current?tagNames=t", handler.LastRequestUri);
    }

    [Fact]
    public async Task ProbeAsync_HttpApi_FollowsExplicitRequestPath()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            """{"data":{"list":[{"value":42.5}]}}""");
        AcquisitionServerProbeService service = CreateService(handler);

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.HttpApi,
            "http://api.local:8080",
            new Dictionary<string, string>
            {
                ["responseValuePath"] = "data.list.0.value"
            },
            "steam_flow",
            "/api/metrics/current");

        Assert.True(result.Success);
        Assert.Equal("42.5", result.ParsedValue);
        Assert.Equal("http://api.local:8080/api/metrics/current", result.RequestTarget);
    }

    [Fact]
    public async Task ProbeAsync_HttpApi_SendsApiKeyHeader()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"value":1}""");
        AcquisitionServerProbeService service = CreateService(handler);

        await service.ProbeAsync(
            AcquisitionServerCatalogKeys.HttpApi,
            "http://api.local:8080",
            new Dictionary<string, string> {["apiKey"] = "secret-key"},
            "steam_flow",
            "/api/current");

        Assert.Equal("secret-key", handler.LastApiKeyHeader);
    }

    [Fact]
    public async Task ProbeAsync_HttpApi_ReportsUpstreamErrorStatus()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.NotFound, """{"error":"not found"}""");
        AcquisitionServerProbeService service = CreateService(handler);

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.HttpApi,
            "http://api.local:8080",
            new Dictionary<string, string>(),
            "steam_flow",
            "/api/missing");

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ProbeAsync_ModbusWithoutIdentifier_FallsBackToNetworkProbe()
    {
        // Modbus 不支持接口读取，未提供标识时应只做网络探测。
        AcquisitionServerProbeService service = CreateService();

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.Modbus,
            "127.0.0.1:1",
            new Dictionary<string, string>());

        Assert.Equal("网络探测", result.ProbeKind);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ProbeAsync_SerialAddress_SkipsAutomaticProbe()
    {
        AcquisitionServerProbeService service = CreateService();

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.Modbus,
            "COM3",
            new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Contains("串口", result.StatusMessage);
    }

    private static AcquisitionServerProbeService CreateService()
    {
        return CreateService(new StubHttpMessageHandler(HttpStatusCode.OK, "{}"));
    }

    private static AcquisitionServerProbeService CreateService(HttpMessageHandler handler)
    {
        var httpClientFactory = new StubHttpClientFactory(handler);
        var historyProxy = new HistoryApiProxyService(
            httpClientFactory,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new AcquisitionServerRuntimeOptions()),
            NullLogger<HistoryApiProxyService>.Instance);

        return new AcquisitionServerProbeService(
            httpClientFactory,
            historyProxy,
            NullLogger<AcquisitionServerProbeService>.Instance);
    }

    /// <summary>
    /// 服务器类型键别名，避免测试文件额外引入目录命名空间。
    /// </summary>
    private static class AcquisitionServerCatalogKeys
    {
        public const string Historian = "Historian API";
        public const string HttpApi = "HTTP API";
        public const string Modbus = "Modbus";
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, false);
        }
    }

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string payload) : HttpMessageHandler
    {
        public string? LastRequestUri { get; private set; }

        public string? LastApiKeyHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();

            LastApiKeyHeader = request.Headers.TryGetValues("X-Api-Key", out IEnumerable<string>? values)
                ? values.FirstOrDefault()
                : null;

            // Token 接口返回可用的访问令牌，其余请求返回预置报文。
            if (request.RequestUri?.AbsolutePath.EndsWith("/uaa/oauth/token") == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"test-token","expires_in":3600}""",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
