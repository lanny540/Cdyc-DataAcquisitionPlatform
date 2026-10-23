using System.Text.Json;
using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 定义采集服务器配置管理能力。
/// </summary>
public interface IAcquisitionServerService
{
    /// <summary>
    /// 获取采集服务器列表。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>服务器配置集合。</returns>
    Task<IReadOnlyCollection<AcquisitionServerDto>> GetServersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定采集服务器配置。
    /// </summary>
    /// <param name="id">服务器标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>服务器配置，不存在时返回 <see langword="null"/>。</returns>
    Task<AcquisitionServerDto?> GetServerAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 新增或更新采集服务器配置。
    /// </summary>
    /// <param name="request">服务器请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>保存后的服务器配置。</returns>
    Task<AcquisitionServerDto> UpsertServerAsync(
        AcquisitionServerUpsertRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定采集服务器配置。
    /// </summary>
    /// <param name="id">服务器标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除结果，被节点引用时返回失败原因。</returns>
    Task<(bool Succeeded, string? ErrorMessage)> DeleteServerAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试采集服务器连通性并读取返回值，同时回写最近一次测试结果。
    /// </summary>
    /// <param name="request">服务器请求，可为尚未保存的草稿。</param>
    /// <param name="identifier">节点数据标识。</param>
    /// <param name="requestPath">接口请求路径。</param>
    /// <param name="nodeConfiguration">节点级配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>测试结果。</returns>
    Task<AcquisitionServerProbeResultDto> TestServerAsync(
        AcquisitionServerUpsertRequest request,
        string? identifier = null,
        string? requestPath = null,
        IReadOnlyDictionary<string, string>? nodeConfiguration = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 提供采集服务器配置管理实现。
/// </summary>
public sealed class AcquisitionServerService(
    DataAcquisitionPlatformDbContext dbContext,
    IAcquisitionServerProbeService probeService,
    ILogger<AcquisitionServerService> logger) : IAcquisitionServerService
{
    /// <summary>密钥字段回显时使用的掩码。</summary>
    public const string SecretMask = "********";

    public async Task<IReadOnlyCollection<AcquisitionServerDto>> GetServersAsync(
        CancellationToken cancellationToken = default)
    {
        ServerConnection[] servers = await dbContext.ServerConnections
            .AsNoTracking()
            .OrderBy(item => item.AcquisitionType)
            .ThenBy(item => item.DisplayName)
            .ToArrayAsync(cancellationToken);

        Dictionary<Guid, int> nodeCounts = await GetNodeCountsAsync(cancellationToken);

        return servers
            .Select(server => Map(server, nodeCounts.GetValueOrDefault(server.Id)))
            .ToArray();
    }

    public async Task<AcquisitionServerDto?> GetServerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ServerConnection? server = await dbContext.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (server is null)
        {
            return null;
        }

        var nodeCount = await dbContext.ManagedDataDefinitions
            .AsNoTracking()
            .CountAsync(item => item.ServerConnectionId == id, cancellationToken);

        return Map(server, nodeCount);
    }

    public async Task<AcquisitionServerDto> UpsertServerAsync(
        AcquisitionServerUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var serverType = request.ServerType.Trim();
        AcquisitionServerTypeDescriptor descriptor =
            AcquisitionServerCatalog.Find(serverType) ??
            throw new InvalidOperationException($"不支持的采集服务器类型：{serverType}。");

        var address = AcquisitionServerCatalog.NormalizeAddress(request.Address);
        Dictionary<string, string> configuration =
            AcquisitionServerCatalog.MergeWithDefaults(serverType, request.Configuration);

        ServerConnection? existing = request.Id.HasValue
            ? await dbContext.ServerConnections.FirstOrDefaultAsync(
                item => item.Id == request.Id.Value, cancellationToken)
            : null;

        existing ??= await dbContext.ServerConnections.FirstOrDefaultAsync(
            item => item.AcquisitionType == serverType && item.Address == address, cancellationToken);

        // 密钥字段回显为掩码时表示"保持原值"，避免因掩码覆盖真实密钥。
        if (existing is not null)
        {
            foreach (var secretKey in configuration.Keys.Where(key => IsSecretKey(serverType, key)).ToArray())
            {
                if (string.Equals(configuration[secretKey], SecretMask, StringComparison.Ordinal) &&
                    existing.ConfigurationJson.Contains(secretKey))
                {
                    var storedValue = ParseConfiguration(existing.ConfigurationJson)
                        .GetValueOrDefault(secretKey);
                    if (!string.IsNullOrWhiteSpace(storedValue))
                    {
                        configuration[secretKey] = storedValue;
                    }
                }
            }
        }

        IReadOnlyCollection<string> errors =
            AcquisitionServerCatalog.ValidateServer(serverType, address, configuration);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? $"{descriptor.DisplayName} {address}"
            : request.DisplayName.Trim();
        var configurationJson = SerializeConfiguration(configuration);

        if (existing is null)
        {
            existing = new ServerConnection
            {
                Id = request.Id ?? Guid.NewGuid(),
                Code = ResolveCode(request.Code, descriptor, address),
                DisplayName = displayName,
                AcquisitionType = serverType,
                Address = address,
                Description = request.Description.Trim(),
                ConfigurationJson = configurationJson,
                IsEnabled = request.IsEnabled,
                LastProbeStatus = "未测试",
                CreatedAt = now,
                UpdatedAt = now
            };

            await dbContext.ServerConnections.AddAsync(existing, cancellationToken);
        }
        else
        {
            existing.DisplayName = displayName;
            existing.AcquisitionType = serverType;
            existing.Address = address;
            existing.Description = request.Description.Trim();
            existing.ConfigurationJson = configurationJson;
            existing.IsEnabled = request.IsEnabled;
            existing.UpdatedAt = now;
        }

        await SaveChangesAsync(existing.Code, cancellationToken);

        // 地址或类型变化后，同步刷新引用该服务器的节点连接地址。
        await SyncRelatedNodesAsync(existing, cancellationToken);

        var nodeCount = await dbContext.ManagedDataDefinitions
            .AsNoTracking()
            .CountAsync(item => item.ServerConnectionId == existing.Id, cancellationToken);

        return Map(existing, nodeCount);
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteServerAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ServerConnection? server = await dbContext.ServerConnections
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (server is null)
        {
            return (false, "采集服务器不存在或已被删除。");
        }

        var nodeCount = await dbContext.ManagedDataDefinitions
            .AsNoTracking()
            .CountAsync(item => item.ServerConnectionId == id, cancellationToken);

        if (nodeCount > 0)
        {
            return (false, $"该服务器仍被 {nodeCount} 个采集节点引用，请先调整这些节点的服务器绑定。");
        }

        dbContext.ServerConnections.Remove(server);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<AcquisitionServerProbeResultDto> TestServerAsync(
        AcquisitionServerUpsertRequest request,
        string? identifier = null,
        string? requestPath = null,
        IReadOnlyDictionary<string, string>? nodeConfiguration = null,
        CancellationToken cancellationToken = default)
    {
        var serverType = request.ServerType.Trim();
        var address = AcquisitionServerCatalog.NormalizeAddress(request.Address);
        Dictionary<string, string> configuration =
            AcquisitionServerCatalog.MergeWithDefaults(serverType, request.Configuration);

        // 测试已保存的服务器时，请求体可能只带掩码占位（甚至为空）。
        if (request.Id.HasValue)
        {
            ServerConnection? stored = await dbContext.ServerConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == request.Id.Value, cancellationToken);

            if (stored is not null)
            {
                serverType = string.IsNullOrWhiteSpace(serverType)
                    ? stored.AcquisitionType.Trim()
                    : serverType;

                address = string.IsNullOrWhiteSpace(address)
                    ? AcquisitionServerCatalog.NormalizeAddress(stored.Address)
                    : address;

                Dictionary<string, string> storedConfiguration = ParseConfiguration(stored.ConfigurationJson);
                configuration = AcquisitionServerCatalog.MergeWithDefaults(serverType, storedConfiguration);

                foreach (KeyValuePair<string, string> item in request.Configuration ??
                                                              new Dictionary<string, string>(StringComparer
                                                                  .OrdinalIgnoreCase))
                {
                    // 掩码占位不覆盖库中真实密钥；其余提交值以本次输入为准。
                    if (string.Equals(item.Value, SecretMask, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    configuration[item.Key] = item.Value;
                }

                foreach (KeyValuePair<string, string> item in storedConfiguration)
                {
                    if (string.Equals(item.Value, SecretMask, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    configuration[item.Key] = item.Value;
                }
            }
        }

        AcquisitionServerProbeResultDto result = await probeService.ProbeAsync(
            serverType, address, configuration, identifier, requestPath, nodeConfiguration, cancellationToken);

        if (request.Id.HasValue)
        {
            await PersistProbeResultAsync(request.Id.Value, result, cancellationToken);
        }

        return result;
    }

    private async Task PersistProbeResultAsync(
        Guid id,
        AcquisitionServerProbeResultDto result,
        CancellationToken cancellationToken)
    {
        try
        {
            ServerConnection? server = await dbContext.ServerConnections
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (server is null)
            {
                return;
            }

            server.LastProbeStatus = result.Success ? "成功" : "失败";
            server.LastProbeMessage = Truncate(result.StatusMessage);
            server.LastProbedAt = result.CheckedAt;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // 测试结果回写失败不应影响探测结论本身。
            logger.LogWarning(ex, "回写采集服务器 {Id} 的测试结果失败。", id);
        }
    }

    private async Task SyncRelatedNodesAsync(ServerConnection server, CancellationToken cancellationToken)
    {
        List<ManagedDataDefinition> nodes = await dbContext.ManagedDataDefinitions
            .Where(item => item.ServerConnectionId == server.Id &&
                           item.ConnectionAddress != server.Address)
            .ToListAsync(cancellationToken);

        if (nodes.Count == 0)
        {
            return;
        }

        foreach (ManagedDataDefinition node in nodes)
        {
            node.ConnectionAddress = server.Address;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, int>> GetNodeCountsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.ManagedDataDefinitions
            .AsNoTracking()
            .Where(item => item.ServerConnectionId.HasValue)
            .GroupBy(item => item.ServerConnectionId!.Value)
            .Select(item => new {ServerConnectionId = item.Key, Count = item.Count()})
            .ToDictionaryAsync(item => item.ServerConnectionId, item => item.Count, cancellationToken);
    }

    private async Task SaveChangesAsync(string code, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is Npgsql.PostgresException {SqlState: Npgsql.PostgresErrorCodes.UniqueViolation})
        {
            throw new InvalidOperationException($"采集服务器编码 {code} 或相同地址的服务器已存在。", ex);
        }
    }

    private static AcquisitionServerDto Map(ServerConnection server, int relatedNodeCount)
    {
        AcquisitionServerTypeDescriptor? descriptor = AcquisitionServerCatalog.Find(server.AcquisitionType);

        // 套用类型目录登记的默认值，确保旧记录在界面上也能看到当前生效的超时与证书策略。
        Dictionary<string, string> configuration = AcquisitionServerCatalog.MergeWithDefaults(
            server.AcquisitionType, ParseConfiguration(server.ConfigurationJson));

        AcquisitionServerConfigValueDto[] configValues = descriptor is null
            ? configuration.Select(item => new AcquisitionServerConfigValueDto(
                item.Key, item.Value, false, !string.IsNullOrWhiteSpace(item.Value))).ToArray()
            : descriptor.ServerFields.Select(field =>
            {
                configuration.TryGetValue(field.Key, out var value);
                var isSecret = field.FieldType == AcquisitionServerFieldTypes.Password;
                var hasValue = !string.IsNullOrWhiteSpace(value);

                return new AcquisitionServerConfigValueDto(
                    field.Key,
                    hasValue && isSecret ? SecretMask : value ?? string.Empty,
                    isSecret,
                    hasValue);
            }).ToArray();

        return new AcquisitionServerDto(
            server.Id,
            server.Code,
            server.DisplayName,
            server.Description,
            server.AcquisitionType,
            descriptor?.DisplayName ?? server.AcquisitionType,
            server.Address,
            server.IsEnabled,
            configValues,
            relatedNodeCount,
            string.IsNullOrWhiteSpace(server.LastProbeStatus) ? "未测试" : server.LastProbeStatus,
            server.LastProbeMessage,
            server.LastProbedAt,
            server.CreatedAt,
            server.UpdatedAt);
    }

    private static bool IsSecretKey(string serverType, string key)
    {
        return AcquisitionServerCatalog.IsServerSecret(serverType, key);
    }

    private static string ResolveCode(
        string? requestedCode,
        AcquisitionServerTypeDescriptor descriptor,
        string address)
    {
        if (!string.IsNullOrWhiteSpace(requestedCode))
        {
            return requestedCode.Trim().ToUpperInvariant();
        }

        var suffix = new string(address
            .Replace("://", string.Empty, StringComparison.Ordinal)
            .Where(char.IsLetterOrDigit)
            .ToArray());

        var prefix = descriptor.ServerType
            .Replace(" API", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        var code = $"SRV_{prefix}{(string.IsNullOrEmpty(suffix) ? "SERVER" : suffix)}";

        // 编码有 64 字符上限与全大写约束。
        return code.Length <= 64 ? code : code[..64];
    }

    private static string SerializeConfiguration(IReadOnlyDictionary<string, string> configuration)
    {
        Dictionary<string, string> normalized = configuration
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(item => item.Key.Trim(), item => item.Value ?? string.Empty, StringComparer.Ordinal);

        return JsonSerializer.Serialize(normalized);
    }

    private static Dictionary<string, string> ParseConfiguration(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var parsed =
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            return parsed?.ToDictionary(
                       item => item.Key,
                       item => item.Value.ValueKind == JsonValueKind.String
                           ? item.Value.GetString() ?? string.Empty
                           : item.Value.ToString(),
                       StringComparer.OrdinalIgnoreCase) ??
                   new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Truncate(string value)
    {
        const int maxLength = 1000;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
