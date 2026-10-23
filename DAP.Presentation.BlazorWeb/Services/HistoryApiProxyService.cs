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
    private const int MinimumTimeoutSeconds = 5;
    private readonly AcquisitionServerRuntimeOptions _runtimeOptions = runtimeOptions.Value;

    public Task<HistoryApiQueryResponse> GetCurrentValueAsync(
        HistoryCurrentValueQueryRequest request,
        HistoryApiConnectionOptions connectionOptions,
        CancellationToken cancellationToken = default)
    {
        var trimmedTagNames = request.TagNames.Trim();
        var currentValuePath = ResolvePath(connectionOptions.CurrentValuePath, "currentValuePath");
        var requestUri = $"{NormalizePath(currentValuePath)}?tagNames={Uri.EscapeDataString(trimmedTagNames)}";

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
        var tagName = request.TagName.Trim();
        var rawDataPath = ResolvePath(connectionOptions.RawDataPathPrefix, "rawDataPathPrefix");
        var requestUri =
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
        var resolvedBaseAddress = connectionOptions.BaseAddress?.Trim() ?? string.Empty;
        var useMockResponses = connectionOptions.UseMockResponses ?? _runtimeOptions.UseMockResponses;

        if (useMockResponses)
        {
            return CreateMockResponse(queryType, requestUri, resolvedBaseAddress, requestedTagNames);
        }

        // 提到 try 之外，便于 catch 区分「自身超时」与「调用方取消」。
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(
            Math.Max(connectionOptions.RequestTimeoutSeconds ?? _runtimeOptions.DefaultRequestTimeoutSeconds,
                MinimumTimeoutSeconds)));

        try
        {
            ValidateConfiguration(resolvedBaseAddress, connectionOptions);

            using HttpClient client = CreateClient(resolvedBaseAddress, connectionOptions);

            var accessToken = await GetAccessTokenAsync(
                client,
                resolvedBaseAddress,
                connectionOptions,
                timeoutSource.Token);

            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using HttpResponseMessage response = await client.SendAsync(requestMessage, timeoutSource.Token);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            return new HistoryApiQueryResponse(
                response.IsSuccessStatusCode,
                queryType,
                new Uri(client.BaseAddress!, requestUri).ToString(),
                (int) response.StatusCode,
                string.IsNullOrWhiteSpace(payload) ? "{}" : payload,
                DateTimeOffset.UtcNow,
                response.IsSuccessStatusCode
                    ? null
                    : $"上游接口返回 {(int) response.StatusCode} {response.ReasonPhrase}");
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
        var currentValue = 12.10m + (now.Second % 17 * 0.07m);
        var midValue = currentValue - 0.11m;
        var earlyValue = currentValue - 0.24m;
        var requestedTags = ResolveRequestedTags(requestedTagNames);
        MockHistorySample[] payloadSamples = queryType == "时间段原始数据"
            ? requestedTags
                .SelectMany(tagName => CreateRawMockSamples(tagName, now, earlyValue, midValue, currentValue))
                .ToArray()
            : requestedTags.Select((tagName, index) => new MockHistorySample(
                    tagName,
                    currentValue + (index * 0.03m),
                    now.ToString("O", CultureInfo.InvariantCulture)))
                .ToArray();

        var payload = JsonSerializer.Serialize(payloadSamples);

        var requestTarget = string.IsNullOrWhiteSpace(resolvedBaseAddress)
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
        var tags = requestedTagNames
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
        var clientId = connectionOptions.ClientId?.Trim() ?? string.Empty;
        var tokenCacheKey = $"{TokenCacheKey}:{resolvedBaseAddress}:{clientId}";

        if (memoryCache.TryGetValue<HistoryApiTokenCacheEntry>(tokenCacheKey,
                out HistoryApiTokenCacheEntry? cachedEntry) &&
            cachedEntry is not null &&
            cachedEntry.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return cachedEntry.AccessToken;
        }

        var oauthTokenPath = ResolvePath(connectionOptions.OAuthTokenPath, "oauthTokenPath");
        using var request = new HttpRequestMessage(HttpMethod.Post, NormalizePath(oauthTokenPath))
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            ])
        };

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{clientId}:{connectionOptions.ClientSecret?.Trim()}")));

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"获取 OAuth Token 失败，上游返回 {(int) response.StatusCode} {response.ReasonPhrase}。{payload}");
        }

        var tokenResponse =
            JsonSerializer.Deserialize<HistoryApiTokenResponse>(payload);

        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException("获取 OAuth Token 失败，服务端未返回有效的 access_token。");
        }

        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(tokenResponse.ExpiresIn - 60, 60));
        var cacheEntry = new HistoryApiTokenCacheEntry(tokenResponse.AccessToken, expiresAt);
        memoryCache.Set(tokenCacheKey, cacheEntry, expiresAt);

        return tokenResponse.AccessToken;
    }

    private static void ValidateConfiguration(string resolvedBaseAddress, HistoryApiConnectionOptions connectionOptions)
    {
        if (string.IsNullOrWhiteSpace(resolvedBaseAddress))
        {
            throw new InvalidOperationException("采集服务器未配置基础地址，请在采集服务器配置页面填写后再测试。");
        }

        if (string.IsNullOrWhiteSpace(connectionOptions.ClientId) ||
            string.IsNullOrWhiteSpace(connectionOptions.ClientSecret))
        {
            throw new InvalidOperationException("采集服务器未配置 ClientId 或 ClientSecret，请在采集服务器配置页面填写后再测试。");
        }
    }

    private HttpClient CreateClient(string resolvedBaseAddress, HistoryApiConnectionOptions connectionOptions)
    {
        if (connectionOptions.IgnoreServerCertificateErrors == true)
        {
            // 自签名证书场景：改用允许无效证书的处理器重新创建客户端。
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            var lenientClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(AppendTrailingSlash(resolvedBaseAddress)),
                Timeout = Timeout.InfiniteTimeSpan
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
    /// 接口路径优先取数据库中的服务器配置，缺失时回落到类型目录登记的默认值。
    /// </summary>
    private static string ResolvePath(string? configuredPath, string configurationKey)
    {
        return string.IsNullOrWhiteSpace(configuredPath)
            ? AcquisitionServerCatalog.GetServerFieldDefault(AcquisitionServerCatalog.Historian, configurationKey)
            : configuredPath.Trim();
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
public sealed record HistoryApiConnectionOptions(
    string? BaseAddress,
    string? ClientId = null,
    string? ClientSecret = null,
    bool? UseMockResponses = null,
    string? OAuthTokenPath = null,
    string? CurrentValuePath = null,
    string? RawDataPathPrefix = null,
    int? RequestTimeoutSeconds = null,
    bool? IgnoreServerCertificateErrors = null);

internal sealed record HistoryApiTokenCacheEntry(string AccessToken, DateTimeOffset ExpiresAt);

internal sealed record MockHistorySample(string tagName, decimal value, string timestamp);

internal sealed record HistoryApiTokenResponse(
    [property: JsonPropertyName("access_token")]
    string AccessToken,
    [property: JsonPropertyName("expires_in")]
    int ExpiresIn);
