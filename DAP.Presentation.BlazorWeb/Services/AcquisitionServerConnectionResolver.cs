using System.Text.Json;
using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 定义采集服务器连接参数的读取能力。
/// 服务器地址、凭据与接口路径统一保存在数据库的采集服务器表中。
/// </summary>
public interface IAcquisitionServerConnectionResolver
{
    /// <summary>
    /// 读取指定采集服务器的连接参数。
    /// </summary>
    /// <param name="serverConnectionId">服务器标识，为空时取第一个启用的该类型服务器。</param>
    /// <param name="serverType">服务器类型键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>连接参数，未配置时返回 <see langword="null"/>。</returns>
    Task<ServerConnectionSettings?> ResolveAsync(
        Guid? serverConnectionId,
        string serverType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按标识读取采集服务器连接参数，不做类型过滤。
    /// </summary>
    /// <param name="serverConnectionId">服务器标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>连接参数，不存在时返回 <see langword="null"/>。</returns>
    Task<ServerConnectionSettings?> ResolveByIdAsync(
        Guid serverConnectionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 表示从数据库读取到的采集服务器连接参数。
/// </summary>
/// <param name="Id">服务器标识。</param>
/// <param name="Code">服务器编码。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Address">服务器基础地址。</param>
/// <param name="Configuration">服务器级配置。</param>
public sealed record ServerConnectionSettings(
    Guid Id,
    string Code,
    string DisplayName,
    string Address,
    IReadOnlyDictionary<string, string> Configuration)
{
    /// <summary>
    /// 转换为 Historian 代理所需的连接参数。
    /// </summary>
    public HistoryApiConnectionOptions ToHistorianOptions()
    {
        return new HistoryApiConnectionOptions(
            Address,
            GetValue("clientId"),
            GetValue("clientSecret"),
            GetBoolean("useMockResponses"),
            GetValue("oauthTokenPath"),
            GetValue("currentValuePath"),
            GetValue("rawDataPathPrefix"),
            GetInteger("timeoutSeconds"),
            GetBoolean("ignoreCertificateErrors"));
    }

    private string GetValue(string key)
    {
        return Configuration.TryGetValue(key, out var value) ? value?.Trim() ?? string.Empty : string.Empty;
    }

    private bool? GetBoolean(string key)
    {
        return bool.TryParse(GetValue(key), out var parsed) ? parsed : null;
    }

    private int? GetInteger(string key)
    {
        return int.TryParse(GetValue(key), out var parsed) && parsed > 0 ? parsed : null;
    }
}

/// <summary>
/// 提供基于数据库的采集服务器连接参数读取实现。
/// </summary>
public sealed class AcquisitionServerConnectionResolver(
    DataAcquisitionPlatformDbContext dbContext,
    Security.PlatformSecretProtector secretProtector) : IAcquisitionServerConnectionResolver
{
    public async Task<ServerConnectionSettings?> ResolveAsync(
        Guid? serverConnectionId,
        string serverType,
        CancellationToken cancellationToken = default)
    {
        ServerConnection? connection = null;

        if (serverConnectionId.HasValue)
        {
            connection = await dbContext.ServerConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.Id == serverConnectionId.Value &&
                            item.AcquisitionType == serverType,
                    cancellationToken);
        }

        connection ??= await dbContext.ServerConnections
            .AsNoTracking()
            .Where(item => item.AcquisitionType == serverType && item.IsEnabled)
            .OrderBy(item => item.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);

        return connection is null ? null : Map(connection);
    }

    /// <inheritdoc />
    public async Task<ServerConnectionSettings?> ResolveByIdAsync(
        Guid serverConnectionId,
        CancellationToken cancellationToken = default)
    {
        ServerConnection? connection = await dbContext.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == serverConnectionId, cancellationToken);

        return connection is null ? null : Map(connection);
    }

    private ServerConnectionSettings Map(ServerConnection connection)
    {
        Dictionary<string, string> configuration =
            ParseConfiguration(connection.ConfigurationJson);

        // 凭据在库里是密文：发给上游之前必须还原，否则拿一段 dp:v1:... 去换 token，
        // 现象是「配置看起来都对、上游一直 401」。
        foreach (var key in configuration.Keys.ToArray())
        {
            if (AcquisitionServerCatalog.IsServerSecret(connection.AcquisitionType, key))
            {
                configuration[key] = secretProtector.Unprotect(configuration[key]);
            }
        }

        // 接口路径等可选字段缺失时，套用类型目录登记的默认值，避免运行期拼接出错误地址。
        Dictionary<string, string> merged =
            AcquisitionServerCatalog.MergeWithDefaults(connection.AcquisitionType, configuration);

        return new ServerConnectionSettings(
            connection.Id,
            connection.Code,
            connection.DisplayName,
            AcquisitionServerCatalog.NormalizeAddress(connection.Address),
            merged);
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
}
