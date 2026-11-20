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
            new Dictionary<string, string> { ["useMockResponses"] = "true" },
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

        StubHttpMessageHandler handler = new(HttpStatusCode.OK, payload);
        AcquisitionServerProbeService service = CreateService(handler);

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.Historian,
            "http://10.9.3.54:8080",
            new Dictionary<string, string> { ["clientId"] = "admin", ["clientSecret"] = "Pa55word" },
            "tag001");

        Assert.True(result.Success);
        Assert.Equal("23.456", result.ParsedValue);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public async Task ProbeAsync_UsesConfiguredCurrentValuePath()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """[{"tagName":"t","value":7}]""");
        AcquisitionServerProbeService service = CreateService(handler);

        await service.ProbeAsync(
            AcquisitionServerCatalogKeys.Historian,
            "http://10.9.3.54:8080",
            new Dictionary<string, string>
            {
                ["clientId"] = "admin", ["clientSecret"] = "Pa55word", ["currentValuePath"] = "/custom/current"
            },
            "t");

        // 服务器上配置的接口路径应覆盖默认值。
        Assert.Contains("/custom/current?tagNames=t", handler.LastRequestUri);
    }

    [Fact]
    public async Task ProbeAsync_HttpApi_FollowsExplicitRequestPath()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """{"data":{"list":[{"value":42.5}]}}""");
        AcquisitionServerProbeService service = CreateService(handler);

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.HttpApi,
            "http://api.local:8080",
            new Dictionary<string, string> { ["responseValuePath"] = "data.list.0.value" },
            "steam_flow",
            "/api/metrics/current");

        Assert.True(result.Success);
        Assert.Equal("42.5", result.ParsedValue);
        Assert.Equal("http://api.local:8080/api/metrics/current", result.RequestTarget);
    }

    [Fact]
    public async Task ProbeAsync_HttpApi_SendsApiKeyHeader()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"value":1}""");
        AcquisitionServerProbeService service = CreateService(handler);

        await service.ProbeAsync(
            AcquisitionServerCatalogKeys.HttpApi,
            "http://api.local:8080",
            new Dictionary<string, string> { ["apiKey"] = "secret-key" },
            "steam_flow",
            "/api/current");

        Assert.Equal("secret-key", handler.LastApiKeyHeader);
    }

    [Fact]
    public async Task ProbeAsync_HttpApi_ReportsUpstreamErrorStatus()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NotFound, """{"error":"not found"}""");
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

    [Fact]
    public async Task ProbeAsync_HttpApi_SendsBearerTokenWithCustomHeader()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"value":1}""");
        AcquisitionServerProbeService service = CreateService(handler);

        await service.ProbeAsync(
            AcquisitionServerCatalogKeys.HttpApi,
            "http://api.local:8080",
            new Dictionary<string, string> { ["authToken"] = "token-123", ["authHeaderName"] = "X-Auth" },
            "steam_flow",
            "/api/current");

        // 自定义令牌头应替换默认 Authorization，且值带 Bearer 前缀。
        Assert.Equal("Bearer token-123", handler.GetHeader("X-Auth"));
        Assert.Null(handler.GetHeader("Authorization"));
    }

    [Fact]
    public async Task ProbeAsync_HttpApi_SendsBasicAuthentication()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"value":1}""");
        AcquisitionServerProbeService service = CreateService(handler);

        await service.ProbeAsync(
            AcquisitionServerCatalogKeys.HttpApi,
            "http://api.local:8080",
            new Dictionary<string, string> { ["username"] = "admin", ["password"] = "Pa55word" },
            "steam_flow",
            "/api/current");

        string expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:Pa55word"));
        Assert.Equal(expected, handler.GetHeader("Authorization"));
    }

    [Fact]
    public async Task ProbeAsync_HttpApi_SendsTemplatedRequestBody()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"value":1}""");
        AcquisitionServerProbeService service = CreateService(handler);

        await service.ProbeAsync(
            AcquisitionServerCatalogKeys.HttpApi,
            "http://api.local:8080",
            new Dictionary<string, string> { ["httpMethod"] = "POST", ["requestBody"] = """{"tag":"{identifier}"}""" },
            "steam_flow",
            "/api/current");

        // 请求体模板中的占位符应被当前采集点位标识替换。
        Assert.Equal("""{"tag":"steam_flow"}""", handler.LastRequestBody);
    }

    [Fact]
    public async Task ProbeAsync_HttpApi_NodeApiPathOverridesDefault()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"value":3.14}""");
        AcquisitionServerProbeService service = CreateService(handler);

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.HttpApi,
            "http://api.local:8080",
            new Dictionary<string, string>(),
            "steam_flow",
            null,
            new Dictionary<string, string> { ["apiPath"] = "/node/override" });

        Assert.Contains("/node/override", handler.LastRequestUri);
        Assert.Equal("3.14", result.ParsedValue);
    }

    [Fact]
    public async Task ProbeAsync_IgnoreCertificateErrors_StillReportsProbeResult()
    {
        // 自签名证书场景会换用放行所有证书的客户端；请求发往本地拒绝端口，
        // 探测应快速失败并返回结构化结果，而不是抛异常。
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"value":1}""");
        AcquisitionServerProbeService service = CreateService(handler);

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.HttpApi,
            "http://127.0.0.1:1",
            new Dictionary<string, string> { ["ignoreCertificateErrors"] = "true" },
            "steam_flow",
            "/api/current");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ProbeAsync_HttpApi_ValuePathReadsStringScalar()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"data":{"value":"66.6"}}""");
        AcquisitionServerProbeService service = CreateService(handler);

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.HttpApi,
            "http://api.local:8080",
            new Dictionary<string, string> { ["responseValuePath"] = "data.value" },
            "steam_flow",
            "/api/current");

        Assert.True(result.Success);
        Assert.Equal("66.6", result.ParsedValue);
    }

    [Fact]
    public async Task ProbeAsync_HttpApi_ValuePathMissing_FailsToParse()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"data":{"other":1}}""");
        AcquisitionServerProbeService service = CreateService(handler);

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.HttpApi,
            "http://api.local:8080",
            new Dictionary<string, string> { ["responseValuePath"] = "data.missing" },
            "steam_flow",
            "/api/current");

        // 配置了取值路径但未命中时：连接成功仍算探测成功（Success=true），
        // 但解析值保持为空，由页面提示「未解析到数值」。此处记录实际行为。
        Assert.True(result.Success);
        Assert.Null(result.ParsedValue);
    }

    [Fact]
    public async Task ProbeAsync_HttpApi_WithoutValuePath_FindsFirstNumberInObject()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"status":"ok","value":12.5}""");
        AcquisitionServerProbeService service = CreateService(handler);

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.HttpApi,
            "http://api.local:8080",
            new Dictionary<string, string>(),
            null,
            "/api/current");

        Assert.True(result.Success);
        Assert.Equal("12.5", result.ParsedValue);
    }

    [Fact]
    public async Task ProbeAsync_HttpApi_FirstNumberSkipsNonNumericStrings()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"name":"abc","nested":{"value":"99.9"}}""");
        AcquisitionServerProbeService service = CreateService(handler);

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.HttpApi,
            "http://api.local:8080",
            new Dictionary<string, string>(),
            null,
            "/api/current");

        // 非数字字符串应被跳过，继续在嵌套对象里找数值。
        Assert.True(result.Success);
        Assert.Equal("99.9", result.ParsedValue);
    }

    [Fact]
    public async Task ProbeAsync_HttpApi_FirstNumberScansArrays()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"rows":[{"text":"a"},{"n":7}]}""");
        AcquisitionServerProbeService service = CreateService(handler);

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.HttpApi,
            "http://api.local:8080",
            new Dictionary<string, string>(),
            null,
            "/api/current");

        Assert.True(result.Success);
        Assert.Equal("7", result.ParsedValue);
    }

    [Fact]
    public async Task ProbeAsync_Historian_NoTagNameMatch_FallsBackToFirstNumber()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK,
            """[{"tagName":"a","value":1},{"tagName":"b","value":2}]""");
        AcquisitionServerProbeService service = CreateService(handler);

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.Historian,
            "http://10.9.3.54:8080",
            new Dictionary<string, string> { ["clientId"] = "admin", ["clientSecret"] = "Pa55word" },
            "tag-none");

        // 数组里没有匹配的 tagName 时，退化为取第一个数值，而不是直接失败。
        Assert.True(result.Success);
        Assert.Equal("1", result.ParsedValue);
    }

    [Fact]
    public async Task ProbeAsync_NetworkProbe_HttpUrl_UsesDefaultPort()
    {
        AcquisitionServerProbeService service = CreateService();

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.Modbus,
            "http://127.0.0.1",
            new Dictionary<string, string>());

        // http 地址未写端口时按默认端口 80 探测；端口是否连通取决于本机环境，不断言成败。
        Assert.Equal("网络探测", result.ProbeKind);
    }

    [Fact]
    public async Task ProbeAsync_NetworkProbe_MqttUrl_UsesDefaultMqttPort()
    {
        AcquisitionServerProbeService service = CreateService();

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.Modbus,
            "mqtt://127.0.0.1",
            new Dictionary<string, string>());

        // mqtt 地址未写端口时按默认端口 1883 探测；不断言成败（取决于本机环境）。
        Assert.Equal("网络探测", result.ProbeKind);
    }

    [Fact]
    public async Task ProbeAsync_NetworkProbe_HostWithoutPort_ReturnsFriendlyError()
    {
        AcquisitionServerProbeService service = CreateService();

        AcquisitionServerProbeResultDto result = await service.ProbeAsync(
            AcquisitionServerCatalogKeys.Modbus,
            "plain-host",
            new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Contains("仅支持带主机和端口", result.ErrorMessage);
    }

    private static AcquisitionServerProbeService CreateService()
    {
        return CreateService(new StubHttpMessageHandler(HttpStatusCode.OK, "{}"));
    }

    private static AcquisitionServerProbeService CreateService(HttpMessageHandler handler)
    {
        StubHttpClientFactory httpClientFactory = new(handler);
        HistoryApiProxyService historyProxy = new(
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

        public string? LastRequestBody { get; private set; }

        private Dictionary<string, string> LastHeaders { get; } = new();

        /// <summary>
        /// 取最近一次请求的指定头；不存在时返回 null。
        /// </summary>
        public string? GetHeader(string name)
        {
            return LastHeaders.TryGetValue(name, out string? value) ? value : null;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            LastHeaders.Clear();
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                LastHeaders[header.Key] = string.Join(",", header.Value);
            }

            LastApiKeyHeader = request.Headers.TryGetValues("X-Api-Key", out IEnumerable<string>? values)
                ? values.FirstOrDefault()
                : null;

            // Token 接口返回可用的访问令牌，其余请求返回预置报文。
            if (request.RequestUri?.AbsolutePath.EndsWith("/uaa/oauth/token") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"test-token","expires_in":3600}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }
}
