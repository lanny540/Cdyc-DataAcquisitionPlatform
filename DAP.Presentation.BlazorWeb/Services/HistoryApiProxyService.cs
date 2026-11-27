using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 定义 History 数据库 API 代理能力。
/// 服务器地址、凭据与接口路径全部来自数据库中的采集服务器配置，不再从 appsettings.json 读取。
/// </summary>
public interface IHistoryApiProxyService
{
    /// <summary>
    /// 按指定服务器配置查询最新值。
    /// </summary>
    /// <param name="request">查询请求。</param>
    /// <param name="connectionOptions">数据库中解析出的服务器连接参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>查询响应。</returns>
    Task<HistoryApiQueryResponse> GetCurrentValueAsync(
        HistoryCurrentValueQueryRequest request,
        HistoryApiConnectionOptions connectionOptions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按指定服务器配置查询时间段原始数据。
    /// </summary>
    /// <param name="request">查询请求。</param>
    /// <param name="connectionOptions">数据库中解析出的服务器连接参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>查询响应。</returns>
    Task<HistoryApiQueryResponse> GetRawDataAsync(
        HistoryRawDataQueryRequest request,
        HistoryApiConnectionOptions connectionOptions,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 提供 GE History 数据库 REST API 的服务端代理实现。
/// </summary>
public sealed class HistoryApiProxyService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    IOptions<AcquisitionServerRuntimeOptions> runtimeOptions,
    ILogger<HistoryApiProxyService> logger) : IHistoryApiProxyService
{
    private const string HttpClientName = "HistoryApi";
    private const string TokenCacheKey = "history-api-access-token";
    private const string GrantModeCacheKey = "history-api-grant-mode";
    private const int MinimumTimeoutSeconds = 5;
    private const string PasswordGrant = "password";
    private const string ClientCredentialsGrant = "client_credentials";

    // 接口路径已从表单移除（GE 各环境一致），默认值统一登记在 AcquisitionServerCatalog；
    // 历史记录中存过自定义路径时仍按存储值优先。
    private readonly AcquisitionServerRuntimeOptions _runtimeOptions = runtimeOptions.Value;

    public Task<HistoryApiQueryResponse> GetCurrentValueAsync(
        HistoryCurrentValueQueryRequest request,
        HistoryApiConnectionOptions connectionOptions,
        CancellationToken cancellationToken = default)
    {
        string trimmedTagNames = request.TagNames.Trim();
        string currentValuePath = ResolvePath(connectionOptions.CurrentValuePath, AcquisitionServerCatalog.HistorianDefaultValuePath);
        string requestUri = $"{NormalizePath(currentValuePath)}?tagNames={Uri.EscapeDataString(trimmedTagNames)}";

        return SendAuthorizedGetAsync(
            "最新值",
            requestUri,
            connectionOptions,
            trimmedTagNames,
            cancellationToken);
    }

    public Task<HistoryApiQueryResponse> GetRawDataAsync(
        HistoryRawDataQueryRequest request,
        HistoryApiConnectionOptions connectionOptions,
        CancellationToken cancellationToken = default)
    {
        string tagName = request.TagName.Trim();
        string rawDataPath = ResolvePath(connectionOptions.RawDataPathPrefix, AcquisitionServerCatalog.HistorianDefaultRawDataPathPrefix);
        string requestUri =
            $"{NormalizePath(rawDataPath)}/{Uri.EscapeDataString(tagName)}/{FormatUtcTimestamp(request.StartTime)}/{FormatUtcTimestamp(request.EndTime)}/{request.StartIndex}/{request.Count}";

        return SendAuthorizedGetAsync(
            "时间段原始数据",
            requestUri,
            connectionOptions,
            tagName,
            cancellationToken);
    }

    private async Task<HistoryApiQueryResponse> SendAuthorizedGetAsync(
        string queryType,
        string requestUri,
        HistoryApiConnectionOptions connectionOptions,
        string requestedTagNames,
        CancellationToken cancellationToken)
    {
        string resolvedBaseAddress = connectionOptions.BaseAddress?.Trim() ?? string.Empty;
        bool useMockResponses = connectionOptions.UseMockResponses ?? _runtimeOptions.UseMockResponses;

        if (useMockResponses)
        {
            return CreateMockResponse(queryType, requestUri, resolvedBaseAddress, requestedTagNames);
        }

        // 提到 try 之外，便于 catch 区分「自身超时」与「调用方取消」。
        using CancellationTokenSource
            timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(
            Math.Max(connectionOptions.RequestTimeoutSeconds ?? _runtimeOptions.DefaultRequestTimeoutSeconds,
                MinimumTimeoutSeconds)));

        try
        {
            ValidateConfiguration(resolvedBaseAddress);

            using HttpClient client = CreateClient(resolvedBaseAddress, connectionOptions);

            string accessToken = await GetAccessTokenAsync(
                client,
                resolvedBaseAddress,
                connectionOptions,
                timeoutSource.Token);

            using HttpRequestMessage requestMessage = new(HttpMethod.Get, requestUri);
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using HttpResponseMessage response = await client.SendAsync(requestMessage, timeoutSource.Token);
            string payload = await response.Content.ReadAsStringAsync(cancellationToken);

            return new HistoryApiQueryResponse(
                response.IsSuccessStatusCode,
                queryType,
                new Uri(client.BaseAddress!, requestUri).ToString(),
                (int)response.StatusCode,
                string.IsNullOrWhiteSpace(payload) ? "{}" : payload,
                DateTimeOffset.UtcNow,
                response.IsSuccessStatusCode
                    ? null
                    : $"上游接口返回 {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            // 上游超时以 TaskCanceledException 抛出，直接透传会把
            // "A task was canceled" 这类框架文案暴露到界面。
            logger.LogWarning("调用 History 数据库 {QueryType} 接口超时。", queryType);

            return new HistoryApiQueryResponse(
                false,
                queryType,
                BuildAbsoluteUrl(requestUri, resolvedBaseAddress),
                0,
                "{}",
                DateTimeOffset.UtcNow,
                "采集服务器响应超时，请检查网络连通性或调大超时配置。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("调用 History 数据库 {QueryType} 接口被取消。", queryType);

            return new HistoryApiQueryResponse(
                false,
                queryType,
                BuildAbsoluteUrl(requestUri, resolvedBaseAddress),
                0,
                "{}",
                DateTimeOffset.UtcNow,
                "请求已取消。");
        }
        catch (HttpRequestException ex)
        {
            // 网络层异常的原因写在日志里供排查，界面只给中文结论。
            // 直接透传 ex.Message 会把 "No such host is known. (host:443)" 这类
            // 框架文案暴露给用户，也可能顺带泄漏内网地址。
            logger.LogWarning(ex, "调用 History 数据库 {QueryType} 接口时网络不可达。", queryType);

            return new HistoryApiQueryResponse(
                false,
                queryType,
                BuildAbsoluteUrl(requestUri, resolvedBaseAddress),
                0,
                "{}",
                DateTimeOffset.UtcNow,
                "无法连接到采集服务器，请检查网络连通性与服务器地址配置。");
        }
        catch (InvalidOperationException ex)
        {
            // 这类异常来自配置校验与令牌获取，消息本身已是可读中文，可安全展示。
            logger.LogWarning(ex, "调用 History 数据库 {QueryType} 接口配置不完整。", queryType);

            return new HistoryApiQueryResponse(
                false,
                queryType,
                BuildAbsoluteUrl(requestUri, resolvedBaseAddress),
                0,
                "{}",
                DateTimeOffset.UtcNow,
                ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "调用 History 数据库 {QueryType} 接口失败。", queryType);

            return new HistoryApiQueryResponse(
                false,
                queryType,
                BuildAbsoluteUrl(requestUri, resolvedBaseAddress),
                0,
                "{}",
                DateTimeOffset.UtcNow,
                "调用采集服务器接口失败，请查看服务端日志了解具体原因。");
        }
    }

    private HistoryApiQueryResponse CreateMockResponse(
        string queryType,
        string requestUri,
        string resolvedBaseAddress,
        string requestedTagNames)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        decimal currentValue = 12.10m + (now.Second % 17 * 0.07m);
        decimal midValue = currentValue - 0.11m;
        decimal earlyValue = currentValue - 0.24m;
        string[] requestedTags = ResolveRequestedTags(requestedTagNames);
        MockHistorySample[] payloadSamples = queryType == "时间段原始数据"
            ? requestedTags
                .SelectMany(tagName => CreateRawMockSamples(tagName, now, earlyValue, midValue, currentValue))
                .ToArray()
            : requestedTags.Select((tagName, index) => new MockHistorySample(
                    tagName,
                    currentValue + (index * 0.03m),
                    now.ToString("O", CultureInfo.InvariantCulture)))
                .ToArray();

        string payload = JsonSerializer.Serialize(payloadSamples);

        string requestTarget = string.IsNullOrWhiteSpace(resolvedBaseAddress)
            ? $"mock://history-api{requestUri}"
            : new Uri(new Uri(AppendTrailingSlash(resolvedBaseAddress)), requestUri).ToString();

        return new HistoryApiQueryResponse(
            true,
            queryType,
            requestTarget,
            200,
            payload,
            now,
            "当前为开发环境 Mock 响应，未访问真实 Historian 服务。");
    }

    private static string[] ResolveRequestedTags(string requestedTagNames)
    {
        string[] tags = requestedTagNames
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return tags.Length == 0 ? ["tag001"] : tags;
    }

    private static MockHistorySample[] CreateRawMockSamples(
        string tagName,
        DateTimeOffset now,
        decimal earlyValue,
        decimal midValue,
        decimal currentValue)
    {
        return
        [
            new MockHistorySample(
                tagName,
                earlyValue,
                now.AddMinutes(-10).ToString("O", CultureInfo.InvariantCulture)),
            new MockHistorySample(
                tagName,
                midValue,
                now.AddMinutes(-5).ToString("O", CultureInfo.InvariantCulture)),
            new MockHistorySample(
                tagName,
                currentValue,
                now.ToString("O", CultureInfo.InvariantCulture))
        ];
    }

    private async Task<string> GetAccessTokenAsync(
        HttpClient client,
        string resolvedBaseAddress,
        HistoryApiConnectionOptions connectionOptions,
        CancellationToken cancellationToken)
    {
        // Client 凭据按存储值使用，留空回落 GE 默认对（现场自定义 Client 见表单说明）。
        (string effectiveClientId, string effectiveClientSecret) =
            AcquisitionServerCatalog.ResolveHistorianClientCredentials(
                connectionOptions.ClientId, connectionOptions.ClientSecret);
        // 令牌与授权模式缓存按「地址 + ClientId + OAuth 用户名」隔离：管理员在同一台服务器上
        // 补填或清空用户名密码，就是在切换授权模式，不能继续沿用另一种模式换来的令牌。
        string credentialScope =
            $"{resolvedBaseAddress}:{effectiveClientId}:{connectionOptions.OAuthUsername?.Trim() ?? string.Empty}";
        string tokenCacheKey = $"{TokenCacheKey}:{credentialScope}";

        if (memoryCache.TryGetValue<HistoryApiTokenCacheEntry>(tokenCacheKey,
                out HistoryApiTokenCacheEntry? cachedEntry) &&
            cachedEntry is not null &&
            cachedEntry.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return cachedEntry.AccessToken;
        }

        // 授权模式自适应：现场 UAA 的 Client 允许的授权类型不可预知（GE 默认 Client 走
        // password，管理员自建 Client 常只开 client_credentials），这里按
        // 「password（已填用户名密码时）→ client_credentials」顺序尝试，被上游以
        // 「Unauthorized grant type」拒绝时自动换下一种，协商结果缓存避免每次重复试错。
        string negotiatedGrant = memoryCache.TryGetValue<string>($"{GrantModeCacheKey}:{credentialScope}",
            out string? cachedGrant) &&
            cachedGrant is not null
            ? cachedGrant
            : string.Empty;

        bool hasUserCredentials =
            !string.IsNullOrWhiteSpace(connectionOptions.OAuthUsername?.Trim()) &&
            !string.IsNullOrWhiteSpace(connectionOptions.OAuthPassword?.Trim());

        // 缓存的 password 模式只在用户名密码仍填写时沿用；凭据已被清空就退回 client_credentials。
        List<string> grantAttempts = negotiatedGrant switch
        {
            PasswordGrant when hasUserCredentials => [PasswordGrant],
            ClientCredentialsGrant => [ClientCredentialsGrant],
            _ when hasUserCredentials => [PasswordGrant, ClientCredentialsGrant],
            _ => [ClientCredentialsGrant]
        };

        string oauthTokenPath = ResolvePath(connectionOptions.OAuthTokenPath, AcquisitionServerCatalog.HistorianDefaultOAuthTokenPath);
        string? lastFailurePayload = null;
        int lastFailureStatus = 0;

        foreach (string grantMode in grantAttempts)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, NormalizePath(oauthTokenPath))
            {
                Content = new FormUrlEncodedContent(
                    BuildTokenRequestBody(connectionOptions, grantMode, hasUserCredentials))
            };

            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes($"{effectiveClientId}:{effectiveClientSecret}")));

            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            string payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                // 协商成功：记住该 Client 实际可用的授权模式，后续换令牌不再试错。
                if (negotiatedGrant != grantMode)
                {
                    memoryCache.Set($"{GrantModeCacheKey}:{credentialScope}", grantMode,
                        TimeSpan.FromHours(8));
                }

                HistoryApiTokenResponse? tokenResponse =
                    JsonSerializer.Deserialize<HistoryApiTokenResponse>(payload);

                if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
                {
                    throw new InvalidOperationException("获取 OAuth Token 失败，服务端未返回有效的 access_token。");
                }

                DateTimeOffset expiresAt =
                    DateTimeOffset.UtcNow.AddSeconds(Math.Max(tokenResponse.ExpiresIn - 60, 60));
                HistoryApiTokenCacheEntry cacheEntry = new(tokenResponse.AccessToken, expiresAt);
                memoryCache.Set(tokenCacheKey, cacheEntry, expiresAt);

                return tokenResponse.AccessToken;
            }

            lastFailurePayload = payload;
            lastFailureStatus = (int)response.StatusCode;

            // 仅当上游明确拒绝「授权类型」时切换下一种模式；用户名密码错误（Bad credentials）
            // 换授权模式没有意义，直接走失败处理。
            bool isGrantTypeRejection = payload.Contains("unauthorized grant type",
                StringComparison.OrdinalIgnoreCase);

            if (!isGrantTypeRejection || grantMode == grantAttempts[^1])
            {
                break;
            }

            logger.LogInformation(
                "UAA 拒绝了 {GrantMode} 授权（Client {ClientId}），自动改用下一种授权模式重试。",
                grantMode,
                effectiveClientId);
        }

        // 上游响应体只进日志：它可能包含 IdP 的错误细节、内部主机名或调试信息，
        // 而这条消息会被直接展示到「History API 测试」页面上。
        // 失败时同时记录实际使用的授权模式、OAuth 用户名与 ClientId（不含密码），
        // 便于和外部工具的成功请求逐项对账——"Bad credentials" 几乎都是两侧凭据不一致。
        logger.LogWarning(
            "获取 OAuth Token 失败：上游返回 {StatusCode}，响应体 {Payload}。本次尝试的授权模式：{GrantModes}，OAuth 用户名：{OAuthUsername}，ClientId：{ClientId}。",
            lastFailureStatus,
            lastFailurePayload,
            string.Join(" → ", grantAttempts),
            connectionOptions.OAuthUsername,
            effectiveClientId);

        throw new InvalidOperationException(
            $"获取 OAuth Token 失败，上游返回 {lastFailureStatus}。" +
            $" 已尝试授权模式：{string.Join("、", grantAttempts)}。" +
            " 请核对采集服务器配置中的 ClientId/ClientSecret 与 OAuth 用户名/密码是否与 Historian 管理员提供的一致。");
    }

    /// <summary>
    /// 构造换取令牌的表单体。授权模式由运行时自适应协商：
    /// password 模式对应 Proficy Authentication 用户（GE 默认 Client 即此场景），
    /// client_credentials 模式对应管理员自建的服务级 Client（仅需 Client 对）。
    /// </summary>
    private List<KeyValuePair<string, string>> BuildTokenRequestBody(
        HistoryApiConnectionOptions connectionOptions,
        string grantMode,
        bool hasUserCredentials)
    {
        if (grantMode == PasswordGrant)
        {
            string oauthUsername = connectionOptions.OAuthUsername?.Trim() ?? string.Empty;
            string oauthPassword = connectionOptions.OAuthPassword?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(oauthUsername) || string.IsNullOrWhiteSpace(oauthPassword))
            {
                throw new InvalidOperationException(
                    "请在采集服务器配置中填写 OAuth 用户名与 OAuth 密码（password 授权必需）。");
            }

            return
            [
                new KeyValuePair<string, string>("grant_type", PasswordGrant),
                new KeyValuePair<string, string>("username", oauthUsername),
                new KeyValuePair<string, string>("password", oauthPassword)
            ];
        }

        return [new KeyValuePair<string, string>("grant_type", ClientCredentialsGrant)];
    }

    private static void ValidateConfiguration(string resolvedBaseAddress)
    {
        if (string.IsNullOrWhiteSpace(resolvedBaseAddress))
        {
            throw new InvalidOperationException("采集服务器未配置基础地址，请在采集服务器配置页面填写后再测试。");
        }
    }

    private HttpClient CreateClient(string resolvedBaseAddress, HistoryApiConnectionOptions connectionOptions)
    {
        if (connectionOptions.IgnoreServerCertificateErrors == true)
        {
            // 自签名证书场景：改用允许无效证书的处理器重新创建客户端。
            HttpClientHandler handler = new()
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            HttpClient lenientClient = new(handler)
            {
                BaseAddress = new Uri(AppendTrailingSlash(resolvedBaseAddress)), Timeout = Timeout.InfiniteTimeSpan
            };

            return lenientClient;
        }

        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(AppendTrailingSlash(resolvedBaseAddress));
        return client;
    }

    private static string BuildAbsoluteUrl(string requestUri, string baseAddress)
    {
        return string.IsNullOrWhiteSpace(baseAddress)
            ? requestUri
            : new Uri(new Uri(AppendTrailingSlash(baseAddress)), requestUri).ToString();
    }

    /// <summary>
    /// 接口路径优先取数据库中的服务器配置（历史记录可能存有自定义路径），缺失时使用内置默认值。
    /// </summary>
    private static string ResolvePath(string? configuredPath, string fallback)
    {
        return string.IsNullOrWhiteSpace(configuredPath) ? fallback : configuredPath.Trim();
    }

    private static string FormatUtcTimestamp(DateTimeOffset value)
    {
        return Uri.EscapeDataString(value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture));
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        return path.StartsWith("/", StringComparison.Ordinal) ? path : $"/{path}";
    }

    private static string AppendTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
    }
}

/// <summary>
/// 表示单个 Historian 服务器的连接参数，全部来源于数据库中的采集服务器配置。
/// </summary>
/// <param name="BaseAddress">服务器基础地址。</param>
/// <param name="ClientId">客户端标识。</param>
/// <param name="ClientSecret">客户端密钥。</param>
/// <param name="UseMockResponses">是否使用 Mock 响应。</param>
/// <param name="OAuthTokenPath">令牌接口路径。</param>
/// <param name="CurrentValuePath">最新值接口路径。</param>
/// <param name="RawDataPathPrefix">原始数据路径前缀。</param>
/// <param name="RequestTimeoutSeconds">请求超时秒数。</param>
/// <param name="IgnoreServerCertificateErrors">是否忽略服务端证书错误。</param>
/// <param name="OAuthUsername">password 授权模式的用户名。</param>
/// <param name="OAuthPassword">password 授权模式的密码。</param>
public sealed record HistoryApiConnectionOptions(
    string? BaseAddress,
    string? ClientId = null,
    string? ClientSecret = null,
    bool? UseMockResponses = null,
    string? OAuthTokenPath = null,
    string? CurrentValuePath = null,
    string? RawDataPathPrefix = null,
    int? RequestTimeoutSeconds = null,
    bool? IgnoreServerCertificateErrors = null,
    string? OAuthUsername = null,
    string? OAuthPassword = null);

internal sealed record HistoryApiTokenCacheEntry(string AccessToken, DateTimeOffset ExpiresAt);

internal sealed record MockHistorySample(string tagName, decimal value, string timestamp);

internal sealed record HistoryApiTokenResponse(
    [property: JsonPropertyName("access_token")]
    string AccessToken,
    [property: JsonPropertyName("expires_in")]
    int ExpiresIn);
