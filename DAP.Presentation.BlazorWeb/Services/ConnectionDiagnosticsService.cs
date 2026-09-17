using System.Net.Sockets;
using System.Text.Json;
using DAP.Core.Shared.Contracts;
using Microsoft.Extensions.Options;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 定义连接诊断能力。
/// </summary>
public interface IConnectionDiagnosticsService
{
    /// <summary>
    /// 获取外部服务器连接状态。
    /// </summary>
    Task<IReadOnlyCollection<ServerConnectionStatusDto>> GetServerStatusesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试后台数据定义草稿的连接情况。
    /// </summary>
    Task<ManagedDataConnectionTestResultDto> TestManagedDataConnectionAsync(
        ManagedDataDefinitionUpsertRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 提供外部服务连接诊断实现。
/// </summary>
public sealed class ConnectionDiagnosticsService(
    IOptions<HistoryApiOptions> historyApiOptions,
    ILogger<ConnectionDiagnosticsService> logger) : IConnectionDiagnosticsService
{
    private readonly HistoryApiOptions _historyApiOptions = historyApiOptions.Value;

    public async Task<IReadOnlyCollection<ServerConnectionStatusDto>> GetServerStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        return
        [
            await GetHistoryApiStatusAsync(cancellationToken)
        ];
    }

    public async Task<ManagedDataConnectionTestResultDto> TestManagedDataConnectionAsync(
        ManagedDataDefinitionUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var acquisitionType = request.AcquisitionType.Trim();
        var address = request.ConnectionAddress.Trim();

        ManagedDataConnectionTestResultDto? validationResult = ValidateConnectionTestRequest(acquisitionType, address);
        if (validationResult is not null)
        {
            return validationResult;
        }

        validationResult = ValidateModbusConnectionTestRequest(request, acquisitionType, address);
        if (validationResult is not null)
        {
            return validationResult;
        }

        ConnectionProbeResult probeResult = await ProbeAddressAsync(address, cancellationToken);
        return new ManagedDataConnectionTestResultDto(
            acquisitionType,
            address,
            probeResult.Success,
            probeResult.StatusMessage,
            probeResult.CheckedAt,
            probeResult.ErrorMessage);
    }

    private async Task<ServerConnectionStatusDto> GetHistoryApiStatusAsync(CancellationToken cancellationToken)
    {
        var address = _historyApiOptions.BaseAddress.Trim();
        var credentialsConfigured =
            !string.IsNullOrWhiteSpace(_historyApiOptions.ClientId) &&
            !string.IsNullOrWhiteSpace(_historyApiOptions.ClientSecret);

        if (string.IsNullOrWhiteSpace(address))
        {
            return new ServerConnectionStatusDto(
                "history-api",
                "Historian API 服务器",
                string.Empty,
                false,
                false,
                _historyApiOptions.UseMockResponses,
                credentialsConfigured,
                "未配置服务器地址。",
                DateTimeOffset.UtcNow,
                "HistoryApi:BaseAddress 为空。");
        }

        ConnectionProbeResult probeResult = await ProbeAddressAsync(address, cancellationToken);
        var statusMessage = BuildHistoryApiStatusMessage(probeResult.Success);

        return new ServerConnectionStatusDto(
            "history-api",
            "Historian API 服务器",
            address,
            true,
            probeResult.Success,
            _historyApiOptions.UseMockResponses,
            credentialsConfigured,
            statusMessage,
            probeResult.CheckedAt,
            probeResult.ErrorMessage);
    }

    private async Task<ConnectionProbeResult> ProbeAddressAsync(string address, CancellationToken cancellationToken)
    {
        if (!TryResolveEndpoint(address, out var host, out var port, out var normalizedAddress, out var errorMessage))
        {
            return CreateProbeFailureResult(normalizedAddress, errorMessage);
        }

        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedSource.CancelAfter(TimeSpan.FromSeconds(Math.Max(_historyApiOptions.RequestTimeoutSeconds, 5)));

        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(host, port, linkedSource.Token);
            return new ConnectionProbeResult(
                true,
                normalizedAddress,
                $"已成功连接到 {host}:{port}。",
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "探测服务器 {Address} 失败。", address);
            return new ConnectionProbeResult(
                false,
                normalizedAddress,
                $"无法连接到 {host}:{port}。",
                DateTimeOffset.UtcNow,
                ex.Message);
        }
    }

    private static ManagedDataConnectionTestResultDto? ValidateConnectionTestRequest(
        string acquisitionType,
        string address)
    {
        return string.IsNullOrWhiteSpace(acquisitionType) || string.IsNullOrWhiteSpace(address)
            ? new ManagedDataConnectionTestResultDto(
                acquisitionType,
                address,
                false,
                "采集方式和连接地址不能为空。",
                DateTimeOffset.UtcNow,
                "参数不完整。")
            : null;
    }

    private static ManagedDataConnectionTestResultDto? ValidateModbusConnectionTestRequest(
        ManagedDataDefinitionUpsertRequest request,
        string acquisitionType,
        string address)
    {
        if (!string.Equals(acquisitionType, "Modbus", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Dictionary<string, string> configuration = ParseJsonDictionary(request.ConfigurationJson);
        var variant = GetValueOrDefault(configuration, "protocolVariant", "RTU");
        if (!string.Equals(variant, "RTU", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new ManagedDataConnectionTestResultDto(
            acquisitionType,
            address,
            false,
            "当前仅支持基于网络地址的连接测试，Modbus RTU 串口连接暂不支持在 Web 端自动探测。",
            DateTimeOffset.UtcNow,
            "不支持串口自动探测。");
    }

    private string BuildHistoryApiStatusMessage(bool isReachable)
    {
        var statusMessage = isReachable
            ? "服务器网络可达。"
            : "服务器当前不可达或开发环境无法访问。";

        return _historyApiOptions.UseMockResponses
            ? $"{statusMessage} 当前业务执行仍会走 Mock 响应。"
            : statusMessage;
    }

    private static ConnectionProbeResult CreateProbeFailureResult(string normalizedAddress, string? errorMessage)
    {
        return new ConnectionProbeResult(
            false,
            normalizedAddress,
            errorMessage ?? "无法解析服务器地址。",
            DateTimeOffset.UtcNow,
            errorMessage);
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

        if (Uri.TryCreate(normalizedAddress, UriKind.Absolute, out Uri? uri))
        {
            host = uri.Host;
            port = uri.IsDefaultPort ? GetDefaultPort(uri.Scheme) : uri.Port;
            normalizedAddress = uri.ToString();

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

    private static Dictionary<string, string> ParseJsonDictionary(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var rawDictionary =
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            return rawDictionary?.ToDictionary(
                item => item.Key,
                item => item.Value.ValueKind == JsonValueKind.String
                    ? item.Value.GetString() ?? string.Empty
                    : item.Value.ToString(),
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string GetValueOrDefault(IReadOnlyDictionary<string, string> dictionary, string key, string fallback)
    {
        return dictionary.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private sealed record ConnectionProbeResult(
        bool Success,
        string Address,
        string StatusMessage,
        DateTimeOffset CheckedAt,
        string? ErrorMessage = null);
}
