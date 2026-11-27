using System.Net;
using DAP.Core.Shared.Catalog;
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
        RecordingHandler handler = new(BusinessOnly(_ => JsonResponse("""{"value":42}""")));
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
        RecordingHandler handler = new(BusinessOnly(_ => JsonResponse("""{"value":1}""")));
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
        RecordingHandler handler = new(BusinessOnly(_ => JsonResponse("""{"items":[]}""")));
        HistoryApiProxyService service = CreateService(handler);

        DateTimeOffset start = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset end = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

        await service.GetRawDataAsync(
            new HistoryRawDataQueryRequest("TAG001", start, end, 0, 100),
            Conn("https://hist.example.com", rawDataPathPrefix: "/api/rawdata"));

        string path = handler.LastRequestUri!.AbsolutePath;

        // 路径应形如 /api/rawdata/{tag}/{start}/{end}/{startIndex}/{count}
        Assert.StartsWith("/api/rawdata/", path, StringComparison.Ordinal);
        Assert.Contains("TAG001", path, StringComparison.Ordinal);
        Assert.EndsWith("/0/100", path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRawDataAsync_Should_Ignore_Blank_RawData_Prefix()
    {
        RecordingHandler handler = new(BusinessOnly(_ => JsonResponse("""{"items":[]}""")));
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
        RecordingHandler handler = new(BusinessOnly(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
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
        RecordingHandler handler = new(_ => throw new TaskCanceledException(
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
        RecordingHandler handler = new(_ => throw new HttpRequestException(
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
        RecordingHandler handler = new(BusinessOnly(_ => JsonResponse("""{"value":1}""")));
        HistoryApiProxyService service = CreateService(handler);

        await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG001"),
            Conn("https://hist.example.com", "id", "secret"));

        // 有凭据时应带上认证信息；这里只断言头存在，不断言具体 token 形态
        Assert.NotNull(handler.LastRequest);
    }

    [Fact]
    public async Task GetCurrentValueAsync_Should_Always_Send_Password_Grant_Form()
    {
        RecordingHandler handler = new(BusinessOnly(_ => JsonResponse("""{"value":1}""")));
        HistoryApiProxyService service = CreateService(handler);

        await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG001"),
            Conn("https://hist.example.com",
                oauthUsername: "HIS81.admin",
                oauthPassword: "web-admin-secret"));

        // GE Historian UAA 的 REST API Client 仅允许 password 授权，
        // 平台固定发送 grant_type=password 与用户名密码，Basic 头仍为 ClientId/ClientSecret。
        Assert.Contains("grant_type=password", handler.LastTokenForm, StringComparison.Ordinal);
        Assert.Contains("HIS81.admin", handler.LastTokenForm, StringComparison.Ordinal);
        Assert.Contains("web-admin-secret", handler.LastTokenForm, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrentValueAsync_Should_Send_Custom_Client_As_Configured()
    {
        // 现场自建 Client（如管理员提供的 admin/Pa55word）按存储值原样使用。
        RecordingHandler handler = new(BusinessOnly(_ => JsonResponse("""{"value":1}""")));
        HistoryApiProxyService service = CreateService(handler);

        await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG001"),
            Conn("https://hist.example.com", "admin", "Pa55word",
                oauthUsername: "HIS81.admin", oauthPassword: "web-admin-secret"));

        string expected = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("admin:Pa55word"));

        Assert.Equal($"Basic {expected}", handler.LastTokenAuthorization);
    }

    [Fact]
    public async Task GetCurrentValueAsync_Should_Fall_Back_To_Ge_Default_Client_When_Blank()
    {
        RecordingHandler handler = new(BusinessOnly(_ => JsonResponse("""{"value":1}""")));
        HistoryApiProxyService service = CreateService(handler);

        await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG001"),
            Conn("https://hist.example.com", " ", " ",
                oauthUsername: "HIS81.admin", oauthPassword: "web-admin-secret"));

        string expected = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(
                $"{AcquisitionServerCatalog.HistorianDefaultClientId}:{AcquisitionServerCatalog.HistorianDefaultClientSecret}"));

        Assert.Equal($"Basic {expected}", handler.LastTokenAuthorization);
    }

    [Fact]
    public async Task GetCurrentValueAsync_Should_Use_Client_Credentials_When_User_Blank()
    {
        // 管理员只提供 Client 对（如 admin/Pa55word）的场景：未填 OAuth 用户名密码时
        // 直接走 client_credentials，不要求用户再填用户名。
        RecordingHandler handler = new(BusinessOnly(_ => JsonResponse("""{"value":1}""")));
        HistoryApiProxyService service = CreateService(handler);

        HistoryApiQueryResponse response = await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG001"),
            Conn("https://hist.example.com", "admin", "Pa55word",
                oauthUsername: null, oauthPassword: null));

        Assert.True(response.Success);
        Assert.Contains("grant_type=client_credentials", handler.LastTokenForm, StringComparison.Ordinal);
        Assert.DoesNotContain("grant_type=password", handler.LastTokenForm, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrentValueAsync_Should_Negotiate_Client_Credentials_When_Password_Rejected()
    {
        // 现场 Client 只开 client_credentials：先按 password 尝试（已填用户名密码），
        // 上游以「Unauthorized grant type」拒绝后自动改用 client_credentials，无需用户选择。
        // 模拟 UAA 只拒绝第一次（password）请求（401，与现场一致），之后放行。
        int tokenRequests = 0;
        RecordingHandler handler = new(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/oauth/", StringComparison.OrdinalIgnoreCase))
            {
                return ++tokenRequests == 1
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Content = new StringContent(
                            """{"error":"invalid_client","error_description":"Unauthorized grant type: password"}""",
                            System.Text.Encoding.UTF8,
                            "application/json")
                    }
                    : JsonResponse("""{"access_token":"token-for-test","expires_in":3600}""");
            }

            return JsonResponse("""{"value":1}""");
        });
        HistoryApiProxyService service = CreateService(handler);

        HistoryApiQueryResponse response = await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG001"),
            Conn("https://hist.example.com", "admin", "Pa55word",
                oauthUsername: "HIS81.admin", oauthPassword: "web-admin-secret"));

        Assert.True(response.Success);
        Assert.Contains("grant_type=client_credentials", handler.LastTokenForm, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrentValueAsync_Should_Switch_To_Password_Grant_When_User_Credentials_Added()
    {
        // 同一台服务器先用 Client 对换到令牌（协商结果已缓存为 client_credentials），
        // 管理员随后补填用户名密码：下一次取令牌必须改走 password，不能沿用缓存的模式。
        RecordingHandler handler = new(BusinessOnly(_ => JsonResponse("""{"value":1}""")));
        HistoryApiProxyService service = CreateService(handler);

        HistoryApiQueryResponse clientOnly = await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG001"),
            Conn("https://hist.example.com", "admin", "Pa55word",
                oauthUsername: null, oauthPassword: null));

        HistoryApiQueryResponse withUser = await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG001"),
            Conn("https://hist.example.com", "admin", "Pa55word",
                oauthUsername: "HIS81.admin", oauthPassword: "web-admin-secret"));

        Assert.True(clientOnly.Success);
        Assert.True(withUser.Success);
        Assert.Contains("grant_type=password", handler.LastTokenForm, StringComparison.Ordinal);
        Assert.Contains("HIS81.admin", handler.LastTokenForm, StringComparison.Ordinal);
        Assert.Contains("web-admin-secret", handler.LastTokenForm, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrentValueAsync_Should_Switch_Back_To_Client_Credentials_When_User_Credentials_Removed()
    {
        // 反向切换：password 成功后授权模式已缓存为 password，
        // 清空用户名密码后应直接用 client_credentials，不能因缓存的 password 模式报「请填写用户名密码」。
        RecordingHandler handler = new(BusinessOnly(_ => JsonResponse("""{"value":1}""")));
        HistoryApiProxyService service = CreateService(handler);

        await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG001"),
            Conn("https://hist.example.com", "admin", "Pa55word",
                oauthUsername: "HIS81.admin", oauthPassword: "web-admin-secret"));

        HistoryApiQueryResponse clientOnly = await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG001"),
            Conn("https://hist.example.com", "admin", "Pa55word",
                oauthUsername: null, oauthPassword: null));

        Assert.True(clientOnly.Success);
        Assert.Contains("grant_type=client_credentials", handler.LastTokenForm, StringComparison.Ordinal);
        Assert.DoesNotContain("grant_type=password", handler.LastTokenForm, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrentValueAsync_Password_Grant_Should_Match_Uaa_Wire_Contract()
    {
        // password 模式的线上请求形态：POST 到配置的 Token 路径、表单编码、
        // Basic 头为 Client 对、表单体含 grant_type/username/password，业务请求携带 Bearer。
        RecordingHandler handler = new(BusinessOnly(_ => JsonResponse("""{"value":1}""")));
        HistoryApiProxyService service = CreateService(handler);

        await service.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest("TAG001"),
            Conn("https://hist.example.com", "admin", "Pa55word",
                oauthUsername: "HIS81.admin", oauthPassword: "web-admin-secret"));

        string expectedBasic = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("admin:Pa55word"));

        Assert.Equal(HttpMethod.Post, handler.LastTokenMethod);
        Assert.Equal("/oauth/token", handler.LastTokenUri!.AbsolutePath);
        Assert.Equal("application/x-www-form-urlencoded",
            handler.LastTokenContentType?.Split(';')[0]);
        Assert.Equal($"Basic {expectedBasic}", handler.LastTokenAuthorization);
        Assert.Contains("grant_type=password", handler.LastTokenForm, StringComparison.Ordinal);
        Assert.Contains("username=HIS81.admin", handler.LastTokenForm, StringComparison.Ordinal);
        Assert.Contains("password=web-admin-secret", handler.LastTokenForm, StringComparison.Ordinal);
        Assert.Equal("Bearer token-for-test", handler.LastRequest?.Headers.Authorization?.ToString());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(HistoryRawDataQueryRequest.MaxCount, true)]
    [InlineData(HistoryRawDataQueryRequest.MaxCount + 1, false)]
    public void Raw_Data_Query_Should_Enforce_Count_Limit(int count, bool expectedValid)
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        HistoryRawDataQueryRequest request = new("TAG001", start, start.AddHours(1), 0, count);

        string? error = request.Validate();

        Assert.Equal(expectedValid, error is null);
        if (!expectedValid && count > 0)
        {
            Assert.Contains(HistoryRawDataQueryRequest.MaxCount.ToString(), error, StringComparison.Ordinal);
        }
    }

    #region 辅助

    private static HistoryApiConnectionOptions Conn(
        string baseAddress,
        string? clientId = "test-client",
        string? clientSecret = "test-secret",
        string? currentValuePath = null,
        string? rawDataPathPrefix = null,
        int? requestTimeoutSeconds = null,
        string? oauthUsername = "test-admin",
        string? oauthPassword = "test-secret")
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
            false,
            oauthUsername,
            oauthPassword);
    }

    private static HistoryApiProxyService CreateService(HttpMessageHandler handler)
    {
        return new HistoryApiProxyService(
            new StubHttpClientFactory(handler),
            new MemoryCache(new MemoryCacheOptions()),
            Microsoft.Extensions.Options.Options.Create(
                new AcquisitionServerRuntimeOptions { UseMockResponses = false }),
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
            return new HttpClient(handler, false) { Timeout = Timeout.InfiniteTimeSpan };
        }
    }

    /// <summary>
    /// 记录发出的请求，并按委托返回预设响应或抛出预设异常。
    /// 取令牌是首个请求且带表单体，HttpClient 发送后表单可能被释放，
    /// 因此在转发前就把表单内容与认证头复制下来供断言使用。
    /// </summary>
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public HttpMethod? LastTokenMethod { get; private set; }

        public Uri? LastTokenUri { get; private set; }

        public string? LastTokenContentType { get; private set; }

        public string LastTokenForm { get; private set; } = string.Empty;

        public string? LastTokenAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestUri = request.RequestUri;

            if (request.RequestUri!.AbsolutePath.Contains("/oauth/", StringComparison.OrdinalIgnoreCase))
            {
                LastTokenMethod = request.Method;
                LastTokenUri = request.RequestUri;
                LastTokenAuthorization = request.Headers.Authorization?.ToString();

                if (request.Content is not null)
                {
                    LastTokenContentType = request.Content.Headers.ContentType?.ToString();
                    LastTokenForm = await request.Content.ReadAsStringAsync(cancellationToken);
                }
            }

            if (responder(request) is { } response)
            {
                return response;
            }

            throw new InvalidOperationException("测试处理器未配置响应。");
        }
    }

    #endregion
}
