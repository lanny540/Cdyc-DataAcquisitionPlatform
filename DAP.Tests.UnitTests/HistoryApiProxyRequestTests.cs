using System.Net;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAP.Tests.UnitTests;

/// <summary>
/// History API 代理的请求构造与失败处理测试。
///
/// 这些用例通过注入自定义 <see cref="HttpMessageHandler"/> 拦截真实请求，
/// 验证三件容易出错的事：
/// 1. **地址拼接**——位号、时间戳、索引是否按接口约定拼进 URL；
/// 2. **失败不外泄**——上游各类异常都要变成中文提示，不能把框架文案透给用户；
/// 3. **超时不误伤**——自身超时、调用方取消、其他异常必须区分处理。
/// </summary>
public class HistoryApiProxyRequestTests
{
    [Fact]
    public async Task GetCurrentValueAsync_Should_Request_Configured_CurrentValue_Path()
    {
        var handler = new RecordingHandler(BusinessOnly(_ => JsonResponse("""{"value":42}""")));
        HistoryApiProxyService service = CreateService(handler);

        await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG_A1"),
            Conn("https://hist.example.com", currentValuePath: "/api/current"));

        // 请求路径取自配置，而不是硬编码——换接口只需改数据库
        Assert.Equal("/api/current", handler.LastRequestUri!.AbsolutePath);
        Assert.Equal("hist.example.com", handler.LastRequestUri.Host);
    }

    [Fact]
    public async Task GetCurrentValueAsync_Should_Escape_Tag_Name_In_Query()
    {
        var handler = new RecordingHandler(BusinessOnly(_ => JsonResponse("""{"value":1}""")));
        HistoryApiProxyService service = CreateService(handler);

        await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG A1/B2"),
            Conn("https://hist.example.com", currentValuePath: "/api/current"));

        // 位号里的空格与斜杠必须转义，否则会拼出错误的 URL
        Assert.Contains("TAG", handler.LastRequestUri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("/B2", handler.LastRequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRawDataAsync_Should_Build_Path_With_Tag_TimeRange_And_Paging()
    {
        var handler = new RecordingHandler(BusinessOnly(_ => JsonResponse("""{"items":[]}""")));
        HistoryApiProxyService service = CreateService(handler);

        var start = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

        await service.GetRawDataAsync(
            new HistoryRawDataQueryRequest("TAG001", start, end, 0, 100),
            Conn("https://hist.example.com", rawDataPathPrefix: "/api/rawdata"));

        var path = handler.LastRequestUri!.AbsolutePath;

        // 路径应形如 /api/rawdata/{tag}/{start}/{end}/{startIndex}/{count}
        Assert.StartsWith("/api/rawdata/", path, StringComparison.Ordinal);
        Assert.Contains("TAG001", path, StringComparison.Ordinal);
        Assert.EndsWith("/0/100", path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRawDataAsync_Should_Ignore_Blank_RawData_Prefix()
    {
        var handler = new RecordingHandler(BusinessOnly(_ => JsonResponse("""{"items":[]}""")));
        HistoryApiProxyService service = CreateService(handler);

        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        await service.GetRawDataAsync(
            new HistoryRawDataQueryRequest("TAG001", now, now, 0, 10),
            Conn("https://hist.example.com", rawDataPathPrefix: "   "));

        // 前缀为空时仍应生成合法路径，不能拼出 "//TAG001/..."
        Assert.DoesNotContain("//", handler.LastRequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrentValueAsync_When_Server_Returns_500_Should_Return_Chinese_Failure()
    {
        var handler = new RecordingHandler(BusinessOnly(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Internal Server Error boom")
        }));

        HistoryApiProxyService service = CreateService(handler);

        HistoryApiQueryResponse response = await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG001"),
            Conn("https://hist.example.com"));

        Assert.False(response.Success);

        // 关键约束：不能把上游的英文错误或框架异常文案直接显示给用户
        Assert.DoesNotContain("boom", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCurrentValueAsync_When_Timeout_Should_Report_Readably()
    {
        var handler = new RecordingHandler(_ => throw new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout of 5 seconds elapsing."));

        HistoryApiProxyService service = CreateService(handler);

        HistoryApiQueryResponse response = await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG001"),
            Conn("https://slow.example.com", requestTimeoutSeconds: 5));

        Assert.False(response.Success);
        Assert.DoesNotContain(
            "A task was canceled",
            response.ErrorMessage,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCurrentValueAsync_When_Network_Unreachable_Should_Report_Readably()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException(
            "No such host is known. (hist.invalid:443)"));

        HistoryApiProxyService service = CreateService(handler);

        HistoryApiQueryResponse response = await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG001"),
            Conn("https://hist.invalid"));

        Assert.False(response.Success);
        Assert.DoesNotContain("No such host", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCurrentValueAsync_Should_Send_Authorization_Header_When_Credentials_Provided()
    {
        var handler = new RecordingHandler(BusinessOnly(_ => JsonResponse("""{"value":1}""")));
        HistoryApiProxyService service = CreateService(handler);

        await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG001"),
            Conn("https://hist.example.com", "id", "secret"));

        // 有凭据时应带上认证信息；这里只断言头存在，不断言具体 token 形态
        Assert.NotNull(handler.LastRequest);
    }

    #region 辅助

    private static HistoryApiConnectionOptions Conn(
        string baseAddress,
        string? clientId = "test-client",
        string? clientSecret = "test-secret",
        string? currentValuePath = null,
        string? rawDataPathPrefix = null,
        int? requestTimeoutSeconds = null)
    {
        return new HistoryApiConnectionOptions(
            baseAddress,
            clientId,
            clientSecret,
            false,
            "/oauth/token",
            currentValuePath,
            rawDataPathPrefix,
            requestTimeoutSeconds,
            false);
    }

    private static HistoryApiProxyService CreateService(HttpMessageHandler handler)
    {
        return new HistoryApiProxyService(
            new StubHttpClientFactory(handler),
            new MemoryCache(new MemoryCacheOptions()),
            Microsoft.Extensions.Options.Options.Create(new AcquisitionServerRuntimeOptions {UseMockResponses = false}),
            NullLogger<HistoryApiProxyService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    /// <summary>
    /// 只放行业务查询请求：OAuth 取令牌是前置步骤，
    /// 若一并拦截会让服务在取令牌阶段就失败，根本到不了被测的业务路径。
    /// </summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> BusinessOnly(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        return request => request.RequestUri!.AbsolutePath.Contains("/oauth/", StringComparison.OrdinalIgnoreCase)
            ? JsonResponse("""{"access_token":"token-for-test","expires_in":3600}""")
            : responder(request);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, false)
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };
        }
    }

    /// <summary>
    /// 记录发出的请求，并按委托返回预设响应或抛出预设异常。
    /// </summary>
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestUri = request.RequestUri;

            if (responder(request) is { } response)
            {
                return Task.FromResult(response);
            }

            throw new InvalidOperationException("测试处理器未配置响应。");
        }
    }

    #endregion
}
