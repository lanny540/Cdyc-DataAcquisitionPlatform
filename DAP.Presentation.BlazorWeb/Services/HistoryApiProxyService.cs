using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DAP.Core.Shared.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 定义 History 数据库 API 代理能力。
/// </summary>
public interface IHistoryApiProxyService
{
    Task<HistoryApiQueryResponse> GetCurrentValueAsync(
        HistoryCurrentValueQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<HistoryApiQueryResponse> GetCurrentValueAsync(
        HistoryCurrentValueQueryRequest request,
        string? baseAddressOverride,
        CancellationToken cancellationToken = default);

    Task<HistoryApiQueryResponse> GetRawDataAsync(
        HistoryRawDataQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<HistoryApiQueryResponse> GetRawDataAsync(
        HistoryRawDataQueryRequest request,
        string? baseAddressOverride,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 提供 GE History 数据库 REST API 的服务端代理实现。
/// </summary>
public sealed class HistoryApiProxyService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    IOptions<HistoryApiOptions> options,
    ILogger<HistoryApiProxyService> logger) : IHistoryApiProxyService
{
    private const string HttpClientName = "HistoryApi";
    private const string TokenCacheKey = "history-api-access-token";
    private readonly HistoryApiOptions _options = options.Value;

    public Task<HistoryApiQueryResponse> GetCurrentValueAsync(
        HistoryCurrentValueQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        return GetCurrentValueAsync(request, null, cancellationToken);
    }

    public Task<HistoryApiQueryResponse> GetCurrentValueAsync(
        HistoryCurrentValueQueryRequest request,
        string? baseAddressOverride,
        CancellationToken cancellationToken = default)
    {
        var trimmedTagNames = request.TagNames.Trim();
        var requestUri =
            $"{NormalizePath(_options.CurrentValuePath)}?tagNames={Uri.EscapeDataString(trimmedTagNames)}";

        return SendAuthorizedGetAsync(
            "最新值",
            requestUri,
            baseAddressOverride,
            trimmedTagNames,
            cancellationToken);
    }

    public Task<HistoryApiQueryResponse> GetRawDataAsync(
        HistoryRawDataQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        return GetRawDataAsync(request, null, cancellationToken);
    }

    public Task<HistoryApiQueryResponse> GetRawDataAsync(
        HistoryRawDataQueryRequest request,
        string? baseAddressOverride,
        CancellationToken cancellationToken = default)
    {
        string tagName = request.TagName.Trim();
        var requestUri =
            $"{NormalizePath(_options.RawDataPathPrefix)}/{Uri.EscapeDataString(tagName)}/{FormatUtcTimestamp(request.StartTime)}/{FormatUtcTimestamp(request.EndTime)}/{request.StartIndex}/{request.Count}";

        return SendAuthorizedGetAsync(
            "时间段原始数据",
            requestUri,
            baseAddressOverride,
            tagName,
            cancellationToken);
    }

    private async Task<HistoryApiQueryResponse> SendAuthorizedGetAsync(
        string queryType,
        string requestUri,
        string? baseAddressOverride,
        string requestedTagNames,
        CancellationToken cancellationToken)
    {
        try
        {
            string resolvedBaseAddress = ResolveBaseAddress(baseAddressOverride);
            if (_options.UseMockResponses)
            {
                return CreateMockResponse(queryType, requestUri, resolvedBaseAddress, requestedTagNames);
            }

            ValidateConfiguration(resolvedBaseAddress);

            using HttpClient client = CreateClient(resolvedBaseAddress);
            string accessToken = await GetAccessTokenAsync(client, resolvedBaseAddress, cancellationToken);

            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using HttpResponseMessage response = await client.SendAsync(requestMessage, cancellationToken);
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
        catch (Exception ex)
        {
            logger.LogWarning(ex, "调用 History 数据库 {QueryType} 接口失败。", queryType);

            return new HistoryApiQueryResponse(
                false,
                queryType,
                BuildAbsoluteUrl(requestUri, baseAddressOverride),
                0,
                "{}",
                DateTimeOffset.UtcNow,
                ex.Message);
        }
    }

    private HistoryApiQueryResponse CreateMockResponse(
        string queryType,
        string requestUri,
        string resolvedBaseAddress,
        string requestedTagNames)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        decimal currentValue = 12.10m + (now.Second % 17) * 0.07m;
        decimal midValue = currentValue - 0.11m;
        decimal earlyValue = currentValue - 0.24m;
        string[] requestedTags = ResolveRequestedTags(requestedTagNames);
        MockHistorySample[] payloadSamples = queryType == "时间段原始数据"
            ? requestedTags.SelectMany(tagName => CreateRawMockSamples(tagName, now, earlyValue, midValue, currentValue))
                .ToArray()
            : requestedTags.Select((tagName, index) => new MockHistorySample(
                tagName,
                currentValue + index * 0.03m,
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
        CancellationToken cancellationToken)
    {
        string tokenCacheKey = $"{TokenCacheKey}:{resolvedBaseAddress}";

        if (memoryCache.TryGetValue<HistoryApiTokenCacheEntry>(tokenCacheKey, out var cachedEntry) &&
            cachedEntry is not null &&
            cachedEntry.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return cachedEntry.AccessToken;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, NormalizePath(_options.OAuthTokenPath))
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            ])
        };

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"获取 OAuth Token 失败，上游返回 {(int)response.StatusCode} {response.ReasonPhrase}。{payload}");
        }

        HistoryApiTokenResponse? tokenResponse =
            JsonSerializer.Deserialize<HistoryApiTokenResponse>(payload);

        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException("获取 OAuth Token 失败，服务端未返回有效的 access_token。");
        }

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(tokenResponse.ExpiresIn - 60, 60));
        var cacheEntry = new HistoryApiTokenCacheEntry(tokenResponse.AccessToken, expiresAt);
        memoryCache.Set(tokenCacheKey, cacheEntry, expiresAt);

        return tokenResponse.AccessToken;
    }

    private void ValidateConfiguration(string resolvedBaseAddress)
    {
        if (string.IsNullOrWhiteSpace(resolvedBaseAddress))
        {
            throw new InvalidOperationException("未配置 HistoryApi:BaseAddress。");
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new InvalidOperationException("未配置 HistoryApi:ClientId 或 HistoryApi:ClientSecret。");
        }
    }

    private HttpClient CreateClient(string resolvedBaseAddress)
    {
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(AppendTrailingSlash(resolvedBaseAddress));
        return client;
    }

    private string BuildAbsoluteUrl(string requestUri, string? baseAddressOverride)
    {
        string resolvedBaseAddress = ResolveBaseAddress(baseAddressOverride);
        return string.IsNullOrWhiteSpace(resolvedBaseAddress)
            ? requestUri
            : new Uri(new Uri(AppendTrailingSlash(resolvedBaseAddress)), requestUri).ToString();
    }

    private string ResolveBaseAddress(string? baseAddressOverride)
    {
        return string.IsNullOrWhiteSpace(baseAddressOverride)
            ? _options.BaseAddress
            : baseAddressOverride.Trim();
    }

    private static string FormatUtcTimestamp(DateTimeOffset value)
    {
        return Uri.EscapeDataString(value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
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
/// 表示 History 数据库代理配置。
/// </summary>
public sealed class HistoryApiOptions
{
    public const string SectionName = "HistoryApi";

    public string BaseAddress { get; set; } = string.Empty;

    public string OAuthTokenPath { get; set; } = "/uaa/oauth/token";

    public string CurrentValuePath { get; set; } = "/historian-rest-api/v1/datapoints/currentvalue";

    public string RawDataPathPrefix { get; set; } = "/historian-rest-api/v1/datapoints/raw";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public bool UseMockResponses { get; set; }

    public bool IgnoreServerCertificateErrors { get; set; } = true;

    public int RequestTimeoutSeconds { get; set; } = 30;
}

internal sealed record HistoryApiTokenCacheEntry(string AccessToken, DateTimeOffset ExpiresAt);

internal sealed record MockHistorySample(string tagName, decimal value, string timestamp);

internal sealed record HistoryApiTokenResponse(
    [property: JsonPropertyName("access_token")]
    string AccessToken,
    [property: JsonPropertyName("expires_in")]
    int ExpiresIn);
