using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 定义连接诊断能力。
/// </summary>
public interface IConnectionDiagnosticsService
{
    /// <summary>
    /// 获取数据库中登记的外部服务器列表，不执行网络探测。
    /// </summary>
    Task<IReadOnlyCollection<ServerConnectionStatusDto>> GetServerConnectionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查指定外部服务器连接状态。
    /// </summary>
    Task<ServerConnectionStatusDto?> CheckServerStatusAsync(string key, CancellationToken cancellationToken = default);

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
    DataAcquisitionPlatformDbContext dbContext,
    ILogger<ConnectionDiagnosticsService> logger) : IConnectionDiagnosticsService
{
    private const int DefaultRequestTimeoutSeconds = 30;

    public async Task<IReadOnlyCollection<ServerConnectionStatusDto>> GetServerConnectionsAsync(
        CancellationToken cancellationToken = default)
    {
        ServerConnection[] connections = await dbContext.ServerConnections
            .AsNoTracking()
            .OrderBy(item => item.AcquisitionType)
            .ThenBy(item => item.DisplayName)
            .ToArrayAsync(cancellationToken);
        Dictionary<Guid, int> definitionCounts = await GetDefinitionCountsAsync(cancellationToken);

        return connections
            .Select(item => MapServerConnection(
                item,
                definitionCounts.GetValueOrDefault(item.Id)))
            .ToArray();
    }

    public async Task<ServerConnectionStatusDto?> CheckServerStatusAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ServerConnectionStatusDto> servers = await GetServerConnectionsAsync(cancellationToken);
        ServerConnectionStatusDto? server = servers.FirstOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));

        return server is null
            ? null
            : await CheckServerStatusAsync(server, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ServerConnectionStatusDto>> GetServerStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ServerConnectionStatusDto> servers = await GetServerConnectionsAsync(cancellationToken);
        ServerConnectionStatusDto[] statuses = await Task.WhenAll(
            servers.Select(item => CheckServerStatusAsync(item, cancellationToken)));

        return statuses;
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

    private async Task<ServerConnectionStatusDto> CheckServerStatusAsync(
        ServerConnectionStatusDto server,
        CancellationToken cancellationToken)
    {
        if (!server.IsConfigured)
        {
            return server with
            {
                IsChecking = false,
                StatusMessage = "未配置服务器地址。",
                CheckedAt = DateTimeOffset.UtcNow,
                ErrorMessage = "数据库中的连接地址为空。"
            };
        }

        if (IsSerialAddress(server.AcquisitionType, server.Address))
        {
            return server with
            {
                IsChecking = false,
                StatusMessage = "当前仅支持网络地址自动探测，串口连接需在采集端验证。",
                CheckedAt = DateTimeOffset.UtcNow,
                ErrorMessage = "不支持串口自动探测。"
            };
        }

        ConnectionProbeResult probeResult = await ProbeAddressAsync(server.Address, cancellationToken);
        var statusMessage = BuildStatusMessage(server, probeResult.Success);

        return server with
        {
            IsReachable = probeResult.Success,
            IsChecking = false,
            StatusMessage = statusMessage,
            CheckedAt = probeResult.CheckedAt,
            ErrorMessage = probeResult.ErrorMessage
        };
    }

    private async Task<Dictionary<Guid, int>> GetDefinitionCountsAsync(
        CancellationToken cancellationToken)
    {
        return await dbContext.ManagedDataDefinitions
            .AsNoTracking()
            .Where(item => item.ServerConnectionId.HasValue)
            .GroupBy(
                item => item.ServerConnectionId!.Value)
            .Select(item => new {ServerConnectionId = item.Key, Count = item.Count()})
            .ToDictionaryAsync(item => item.ServerConnectionId, item => item.Count, cancellationToken);
    }

    private static ServerConnectionStatusDto MapServerConnection(ServerConnection connection, int relatedDefinitionCount)
    {
        Dictionary<string, string> configuration = ParseJsonDictionary(connection.ConfigurationJson);
        var address = NormalizeAddress(connection.Address);
        var isConfigured = !string.IsNullOrWhiteSpace(address);
        var useMockResponses =
            IsEnabledConfigurationValue(configuration, "useMockResponses") ||
            IsEnabledConfigurationValue(configuration, "useMock");
        var credentialsConfigured = HasCredentials(configuration);

        return new ServerConnectionStatusDto(
            CreateServerKey(connection.AcquisitionType, address),
            connection.DisplayName.Trim(),
            connection.AcquisitionType.Trim(),
            address,
            isConfigured,
            false,
            useMockResponses,
            credentialsConfigured,
            relatedDefinitionCount,
            false,
            isConfigured ? "等待异步检测。" : "未配置服务器地址。",
            DateTimeOffset.UtcNow,
            isConfigured ? null : "数据库中的连接地址为空。");
    }

    private async Task<ConnectionProbeResult> ProbeAddressAsync(string address, CancellationToken cancellationToken)
    {
        if (!TryResolveEndpoint(address, out var host, out var port, out var normalizedAddress, out var errorMessage))
        {
            return CreateProbeFailureResult(normalizedAddress, errorMessage);
        }

        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedSource.CancelAfter(TimeSpan.FromSeconds(DefaultRequestTimeoutSeconds));

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

    private static string BuildStatusMessage(ServerConnectionStatusDto server, bool isReachable)
    {
        var statusMessage = isReachable
            ? "服务器网络可达。"
            : "服务器当前不可达或开发环境无法访问。";

        return server.UseMockResponses
            ? $"{statusMessage} 当前业务执行仍会走 Mock 响应。"
            : statusMessage;
    }

    private static bool IsSerialAddress(string acquisitionType, string address)
    {
        return string.Equals(acquisitionType, "Modbus", StringComparison.OrdinalIgnoreCase) &&
               address.StartsWith("COM", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProtocol(string value)
    {
        return value.Trim();
    }

    private static string NormalizeAddress(string value)
    {
        return value.Trim().TrimEnd('/');
    }

    private static string CreateServerKey(string acquisitionType, string address)
    {
        var payload = $"{acquisitionType.Trim().ToUpperInvariant()}|{address.Trim().ToUpperInvariant()}";
        byte[] hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes[..8]).ToLowerInvariant();
    }

    private static bool HasCredentials(IReadOnlyDictionary<string, string> configuration)
    {
        return HasValue(configuration, "accessToken") ||
               HasValue(configuration, "apiKey") ||
               HasValue(configuration, "token") ||
               HasValue(configuration, "username") && HasValue(configuration, "password") ||
               HasValue(configuration, "clientId") && HasValue(configuration, "clientSecret");
    }

    private static bool IsEnabledConfigurationValue(IReadOnlyDictionary<string, string> configuration, string key)
    {
        return configuration.TryGetValue(key, out var value) &&
               bool.TryParse(value, out var enabled) &&
               enabled;
    }

    private static bool HasValue(IReadOnlyDictionary<string, string> configuration, string key)
    {
        return configuration.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
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

    private sealed record ServerConnectionGroupKey(string AcquisitionType, string Address);
}
