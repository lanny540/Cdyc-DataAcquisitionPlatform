using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 定义采集服务器连通性与接口读取能力。
/// </summary>
public interface IAcquisitionServerProbeService
{
    /// <summary>
    /// 按服务器配置执行连通性或接口读取测试。
    /// </summary>
    /// <param name="serverType">服务器类型键。</param>
    /// <param name="address">服务器基础地址。</param>
    /// <param name="configuration">服务器配置。</param>
    /// <param name="identifier">节点数据标识，接口读取时使用。</param>
    /// <param name="requestPath">接口请求路径，未指定时使用类型默认路径。</param>
    /// <param name="nodeConfiguration">节点级配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>测试结果。</returns>
    Task<AcquisitionServerProbeResultDto> ProbeAsync(
        string serverType,
        string address,
        IReadOnlyDictionary<string, string> configuration,
        string? identifier = null,
        string? requestPath = null,
        IReadOnlyDictionary<string, string>? nodeConfiguration = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 提供采集服务器探测实现，支持网络连通性探测与 HTTP 接口读取。
/// </summary>
public sealed class AcquisitionServerProbeService(
    IHttpClientFactory httpClientFactory,
    IHistoryApiProxyService historyApiProxyService,
    ILogger<AcquisitionServerProbeService> logger) : IAcquisitionServerProbeService
{
    private const string HttpClientName = "AcquisitionServerProbe";
    private const int DefaultTimeoutSeconds = 15;

    public async Task<AcquisitionServerProbeResultDto> ProbeAsync(
        string serverType,
        string address,
        IReadOnlyDictionary<string, string> configuration,
        string? identifier = null,
        string? requestPath = null,
        IReadOnlyDictionary<string, string>? nodeConfiguration = null,
        CancellationToken cancellationToken = default)
    {
        AcquisitionServerTypeDescriptor? descriptor = AcquisitionServerCatalog.Find(serverType);
        var normalizedAddress = AcquisitionServerCatalog.NormalizeAddress(address);

        if (descriptor is null)
        {
            return CreateFailure("不支持的采集服务器类型", serverType, 0, $"不支持的采集服务器类型：{serverType}。");
        }

        if (string.IsNullOrWhiteSpace(normalizedAddress))
        {
            return CreateFailure("网络探测", string.Empty, 0, $"{descriptor.AddressLabel}不能为空。");
        }

        if (IsMockEnabled(configuration))
        {
            return CreateMockResult(descriptor, normalizedAddress, identifier, requestPath);
        }

        // Historian 需要先换取 OAuth2 令牌，复用既有代理服务避免重复实现鉴权。
        if (descriptor.ServerType == AcquisitionServerCatalog.Historian)
        {
            return await ProbeHistorianAsync(normalizedAddress, configuration, identifier, cancellationToken);
        }

        if (descriptor.SupportsApiProbe && ShouldReadApiValue(identifier, requestPath))
        {
            return await ProbeHttpAsync(
                descriptor, normalizedAddress, configuration, identifier, requestPath, nodeConfiguration,
                cancellationToken);
        }

        return await ProbeNetworkAsync(descriptor, normalizedAddress, cancellationToken);
    }

    private async Task<AcquisitionServerProbeResultDto> ProbeHistorianAsync(
        string address,
        IReadOnlyDictionary<string, string> configuration,
        string? identifier,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return await ProbeNetworkAsync(
                AcquisitionServerCatalog.Find(AcquisitionServerCatalog.Historian)!, address, cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();
        var useMockResponses = IsTrue(GetValue(configuration, "useMockResponses", "false"));
        // 探测时配置来自界面提交或数据库记录，不再回落到 appsettings.json。
        var connectionOptions = new HistoryApiConnectionOptions(
            address,
            GetValue(configuration, "clientId", string.Empty),
            GetValue(configuration, "clientSecret", string.Empty),
            useMockResponses,
            GetValue(configuration, "oauthTokenPath", string.Empty),
            GetValue(configuration, "currentValuePath", string.Empty),
            GetValue(configuration, "rawDataPathPrefix", string.Empty),
            GetIntValue(configuration, "timeoutSeconds", DefaultTimeoutSeconds),
            IsTrue(GetValue(configuration, "ignoreCertificateErrors", "false")));

        HistoryApiQueryResponse response = await historyApiProxyService.GetCurrentValueAsync(
            new HistoryCurrentValueQueryRequest(identifier), connectionOptions, cancellationToken);
        stopwatch.Stop();

        var parsedValue = TryExtractValue(response.PayloadJson, configuration, identifier);

        return new AcquisitionServerProbeResultDto(
            response.Success,
            "接口读取",
            response.RequestUrl,
            response.StatusCode,
            response.Success
                ? $"Historian 接口调用成功，返回 {response.PayloadJson.Length} 字节。" +
                  (parsedValue is null ? "未能在返回报文中定位数值。" : $"已解析出数值 {parsedValue}。")
                : response.ErrorMessage ?? $"Historian 接口返回 {(int) response.StatusCode}。",
            string.IsNullOrWhiteSpace(response.PayloadJson) ? "{}" : response.PayloadJson,
            parsedValue,
            stopwatch.ElapsedMilliseconds,
            DateTimeOffset.UtcNow,
            response.Success ? null : response.ErrorMessage);
    }

    private async Task<AcquisitionServerProbeResultDto> ProbeNetworkAsync(
        AcquisitionServerTypeDescriptor descriptor,
        string address,
        CancellationToken cancellationToken)
    {
        // 串口地址无法解析为网络端点，需在端点解析之前拦截。
        if (IsSerialAddress(address))
        {
            return new AcquisitionServerProbeResultDto(
                false,
                "网络探测",
                address,
                0,
                $"{descriptor.DisplayName} 为串口连接，需在采集端验证，当前无法在 Web 端探测。",
                "{}",
                null,
                0,
                DateTimeOffset.UtcNow,
                "不支持串口自动探测。");
        }

        if (!TryResolveEndpoint(address, out var host, out var port, out var normalizedAddress, out var errorMessage))
        {
            return CreateFailure("网络探测", address, 0, errorMessage ?? "无法解析服务器地址。");
        }

        var stopwatch = Stopwatch.StartNew();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(DefaultTimeoutSeconds));

        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(host, port, timeoutSource.Token);
            stopwatch.Stop();

            return new AcquisitionServerProbeResultDto(
                true,
                "网络探测",
                $"{host}:{port}",
                0,
                $"已成功连接到 {host}:{port}。该类型仅支持网络层探测，接口读取需在节点配置中完成。",
                "{}",
                null,
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            // 探测超时：TCP 连接在时限内未建立。
            // 超时异常的消息是英文框架文案，只给中文结论，细节写日志。
            stopwatch.Stop();
            logger.LogInformation("探测服务器 {Address} 超时。", address);

            return new AcquisitionServerProbeResultDto(
                false,
                "网络探测",
                $"{host}:{port}",
                0,
                "连接超时，未能在时限内完成探测。",
                "{}",
                null,
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            // 其余异常不外泄原始英文文案与内网地址，细节只进日志。
            stopwatch.Stop();
            logger.LogInformation(ex, "探测服务器 {Address} 失败。", address);

            return new AcquisitionServerProbeResultDto(
                false,
                "网络探测",
                $"{host}:{port}",
                0,
                $"无法连接到 {host}:{port}，请查看服务端日志。",
                "{}",
                null,
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow);
        }
    }

    private async Task<AcquisitionServerProbeResultDto> ProbeHttpAsync(
        AcquisitionServerTypeDescriptor descriptor,
        string address,
        IReadOnlyDictionary<string, string> configuration,
        string? identifier,
        string? requestPath,
        IReadOnlyDictionary<string, string>? nodeConfiguration,
        CancellationToken cancellationToken)
    {
        var httpMethod = GetValue(configuration, "httpMethod", "GET").ToUpperInvariant();
        var path = ResolveRequestPath(descriptor, requestPath, identifier, configuration, nodeConfiguration);
        var requestTarget = BuildRequestTarget(address, path);

        var stopwatch = Stopwatch.StartNew();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(
            GetIntValue(configuration, "timeoutSeconds", DefaultTimeoutSeconds)));

        try
        {
            using HttpClient client = CreateClient(configuration, address);
            using var request = new HttpRequestMessage(new HttpMethod(httpMethod), path);

            ApplyAuthentication(request, configuration);

            if (httpMethod == "POST")
            {
                request.Content = BuildRequestBody(configuration, identifier);
            }

            using HttpResponseMessage response = await client.SendAsync(request, timeoutSource.Token);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();

            var parsedValue = TryExtractValue(payload, configuration, identifier);

            return new AcquisitionServerProbeResultDto(
                response.IsSuccessStatusCode,
                "接口读取",
                requestTarget,
                (int) response.StatusCode,
                response.IsSuccessStatusCode
                    ? $"接口调用成功，返回 {payload.Length} 字节。" +
                      (parsedValue is null ? "未能在返回报文中定位数值。" : $"已解析出数值 {parsedValue}。")
                    : $"上游接口返回 {(int) response.StatusCode} {response.ReasonPhrase}。",
                string.IsNullOrWhiteSpace(payload) ? "{}" : payload,
                parsedValue,
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow,
                response.IsSuccessStatusCode ? null : TruncatePayload(payload));
        }
        catch (OperationCanceledException)
        {
            // 超时：异常消息是英文框架文案，只给中文结论，细节写日志。
            stopwatch.Stop();
            logger.LogInformation("调用 {ServerType} 接口超时：{Target}", descriptor.ServerType, requestTarget);

            return new AcquisitionServerProbeResultDto(
                false,
                "接口读取",
                requestTarget,
                0,
                "接口调用超时，可适当增大超时时间或检查上游服务状态。",
                "{}",
                null,
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException ex)
        {
            // 网络不可达：原始消息可能含英文框架文案与内网地址，只给中文结论。
            stopwatch.Stop();
            logger.LogInformation(ex, "调用 {ServerType} 接口失败：{Target}", descriptor.ServerType, requestTarget);

            return new AcquisitionServerProbeResultDto(
                false,
                "接口读取",
                requestTarget,
                0,
                "网络不可达，无法连接上游接口，请查看服务端日志。",
                "{}",
                null,
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogInformation(ex, "调用 {ServerType} 接口失败：{Target}", descriptor.ServerType, requestTarget);

            return new AcquisitionServerProbeResultDto(
                false,
                "接口读取",
                requestTarget,
                0,
                "接口调用失败，请查看服务端日志。",
                "{}",
                null,
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow);
        }
    }

    private HttpClient CreateClient(IReadOnlyDictionary<string, string> configuration, string address)
    {
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);

        if (IsTrue(GetValue(configuration, "ignoreCertificateErrors", "false")))
        {
            // 自签名证书场景：改用允许无效证书的处理器重新创建客户端。
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler
                    .DangerousAcceptAnyServerCertificateValidator
            };
            client = new HttpClient(handler) {Timeout = client.Timeout};
        }

        // 接口路径为相对路径，必须先设置基地址，否则请求无法解析为绝对地址。
        if (Uri.TryCreate(address, UriKind.Absolute, out Uri? baseUri))
        {
            client.BaseAddress = new Uri($"{baseUri.GetLeftPart(UriPartial.Authority)}/");
        }

        return client;
    }

    private static void ApplyAuthentication(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, string> configuration)
    {
        var apiKey = GetValue(configuration, "apiKey", string.Empty);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        }

        var authToken = GetValue(configuration, "authToken", string.Empty);
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            var headerName = GetValue(configuration, "authHeaderName", "Authorization");
            request.Headers.Remove(headerName);
            request.Headers.TryAddWithoutValidation(headerName, $"Bearer {authToken}");
        }

        var username = GetValue(configuration, "username", string.Empty);
        var password = GetValue(configuration, "password", string.Empty);
        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
        }
    }

    private static HttpContent? BuildRequestBody(
        IReadOnlyDictionary<string, string> configuration,
        string? identifier)
    {
        var template = GetValue(configuration, "requestBody", string.Empty);
        if (string.IsNullOrWhiteSpace(template))
        {
            return new StringContent("{}", Encoding.UTF8, "application/json");
        }

        var body = template.Replace("{identifier}", identifier ?? string.Empty, StringComparison.Ordinal);
        return new StringContent(body, Encoding.UTF8, "application/json");
    }

    private static string ResolveRequestPath(
        AcquisitionServerTypeDescriptor descriptor,
        string? requestPath,
        string? identifier,
        IReadOnlyDictionary<string, string> configuration,
        IReadOnlyDictionary<string, string>? nodeConfiguration)
    {
        var candidates = new[]
        {
            requestPath,
            nodeConfiguration is not null && nodeConfiguration.TryGetValue("apiPath", out var nodePath)
                ? nodePath
                : null,
            descriptor.DefaultApiProbePath
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return NormalizePath(ExpandPlaceholders(candidate, identifier));
            }
        }

        // 默认路径为空时，回落到类型配置中登记的接口路径。
        var fallback = descriptor.ServerType switch
        {
            AcquisitionServerCatalog.Historian => GetValue(configuration, "currentValuePath", string.Empty),
            AcquisitionServerCatalog.InfluxDb => GetValue(configuration, "healthPath", string.Empty),
            _ => string.Empty
        };

        return NormalizePath(ExpandPlaceholders(
            string.IsNullOrWhiteSpace(fallback) ? "/" : fallback, identifier));
    }

    private static string ExpandPlaceholders(string path, string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return path;
        }

        return path
            .Replace("{tagName}", Uri.EscapeDataString(identifier), StringComparison.Ordinal)
            .Replace("{identifier}", Uri.EscapeDataString(identifier), StringComparison.Ordinal);
    }

    private static bool ShouldReadApiValue(string? identifier, string? requestPath)
    {
        return !string.IsNullOrWhiteSpace(identifier) || !string.IsNullOrWhiteSpace(requestPath);
    }

    private static bool IsMockEnabled(IReadOnlyDictionary<string, string> configuration)
    {
        return IsTrue(GetValue(configuration, "useMockResponses", "false"));
    }

    private static AcquisitionServerProbeResultDto CreateMockResult(
        AcquisitionServerTypeDescriptor descriptor,
        string address,
        string? identifier,
        string? requestPath)
    {
        var path = NormalizePath(
            string.IsNullOrWhiteSpace(requestPath) ? descriptor.DefaultApiProbePath ?? "/" : requestPath);

        return new AcquisitionServerProbeResultDto(
            true,
            "接口读取",
            BuildRequestTarget(address, path),
            200,
            "当前为 Mock 响应，未访问真实服务器。",
            "{\"mock\":true,\"tagName\":\"" + (identifier ?? string.Empty) +
            "\",\"value\":12.34,\"unit\":\"待确认\"}",
            "12.34",
            1,
            DateTimeOffset.UtcNow);
    }

    private static string? TryExtractValue(
        string payload,
        IReadOnlyDictionary<string, string> configuration,
        string? identifier)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var valuePath = GetValue(configuration, "responseValuePath", string.Empty);
        if (!string.IsNullOrWhiteSpace(valuePath))
        {
            return TryReadJsonPath(payload, valuePath);
        }

        if (string.IsNullOrWhiteSpace(identifier))
        {
            return TryReadFirstNumber(payload);
        }

        // GE Historian 最新值返回数组，元素中 tagName 与当前采集点位匹配。
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    var tagName = TryReadStringProperty(element, "tagName");
                    if (tagName is null || !string.Equals(tagName, identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var value = TryReadNumber(element, "value");
                    if (value is not null)
                    {
                        return value;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return TryReadFirstNumber(payload);
        }

        return TryReadFirstNumber(payload);
    }

    private static string? TryReadJsonPath(string payload, string valuePath)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement current = document.RootElement;

            foreach (var segment in valuePath.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out JsonElement next))
                {
                    current = next;
                    continue;
                }

                if (current.ValueKind == JsonValueKind.Array &&
                    int.TryParse(segment, out var index) &&
                    index >= 0 &&
                    index < current.GetArrayLength())
                {
                    current = current[index];
                    continue;
                }

                return null;
            }

            return ReadScalar(current);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadFirstNumber(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            return FindFirstNumber(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindFirstNumber(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.GetRawText();
            case JsonValueKind.String:
                var text = element.GetString();
                return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                    ? text
                    : null;
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, "value", StringComparison.OrdinalIgnoreCase))
                    {
                        var direct = ReadScalar(property.Value);
                        if (direct is not null)
                        {
                            return direct;
                        }
                    }
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    var nested = FindFirstNumber(property.Value);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }

                return null;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    var nested = FindFirstNumber(item);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private static string? ReadScalar(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.String => element.GetString(),
            _ => null
        };
    }

    private static string? TryReadNumber(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) ? ReadScalar(property) : null;
    }

    private static string? TryReadStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool TryResolveEndpoint(
        string address,
        out string host,
        out int port,
        out string normalizedAddress,
        out string? errorMessage)
    {
        host = string.Empty;
        port = 0;
        normalizedAddress = address.Trim();
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(normalizedAddress))
        {
            errorMessage = "地址为空。";
            return false;
        }

        if (Uri.TryCreate(normalizedAddress, UriKind.Absolute, out Uri? uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            host = uri.Host;
            port = uri.IsDefaultPort ? GetDefaultPort(uri.Scheme) : uri.Port;
            normalizedAddress = uri.GetLeftPart(UriPartial.Authority);

            if (string.IsNullOrWhiteSpace(host) || port <= 0)
            {
                errorMessage = "地址缺少可探测的主机名或端口。";
                return false;
            }

            return true;
        }

        var segments =
            normalizedAddress.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2 && int.TryParse(segments[1], out var parsedPort))
        {
            host = segments[0];
            port = parsedPort;
            return !string.IsNullOrWhiteSpace(host) && port > 0;
        }

        errorMessage = "仅支持带主机和端口的网络地址。";
        return false;
    }

    private static int GetDefaultPort(string scheme)
    {
        return scheme.ToLowerInvariant() switch
        {
            "http" => 80,
            "https" => 443,
            "mqtt" => 1883,
            "mqtts" => 8883,
            _ => -1
        };
    }

    private static bool IsSerialAddress(string address)
    {
        return address.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
               address.StartsWith("/dev/tty", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRequestTarget(string address, string path)
    {
        var baseAddress = address.TrimEnd('/');
        var normalizedPath = NormalizePath(path);
        return $"{baseAddress}{normalizedPath}";
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        return path.StartsWith('/') ? path : $"/{path}";
    }

    private static string GetValue(
        IReadOnlyDictionary<string, string> configuration,
        string key,
        string fallback)
    {
        return configuration.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
    }

    private static int GetIntValue(IReadOnlyDictionary<string, string> configuration, string key, int fallback)
    {
        return int.TryParse(GetValue(configuration, key, string.Empty), out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static bool IsTrue(string value)
    {
        return bool.TryParse(value, out var parsed) && parsed;
    }

    private static AcquisitionServerProbeResultDto CreateFailure(
        string probeKind,
        string target,
        int statusCode,
        string errorMessage)
    {
        return new AcquisitionServerProbeResultDto(
            false,
            probeKind,
            target,
            statusCode,
            errorMessage,
            "{}",
            null,
            0,
            DateTimeOffset.UtcNow,
            errorMessage);
    }

    private static string TruncatePayload(string payload)
    {
        const int maxLength = 500;
        return payload.Length <= maxLength ? payload : $"{payload[..maxLength]}...";
    }
}
