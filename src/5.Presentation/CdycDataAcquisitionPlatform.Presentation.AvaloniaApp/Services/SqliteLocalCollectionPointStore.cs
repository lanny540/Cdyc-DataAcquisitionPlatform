using CdycDataAcquisitionPlatform.Core.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace CdycDataAcquisitionPlatform.Presentation.AvaloniaApp.Services;

/// <summary>
/// 提供桌面客户端本地采集点配置的 SQLite 存储。
/// </summary>
public sealed class SqliteLocalCollectionPointStore
{
    private readonly string _connectionString;

    /// <summary>
    /// 初始化一个新的 <see cref="SqliteLocalCollectionPointStore"/> 实例。
    /// </summary>
    /// <param name="options">本地存储配置。</param>
    public SqliteLocalCollectionPointStore(IOptions<LocalStorageOptions> options)
    {
        var appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CdycDataAcquisitionPlatform");

        Directory.CreateDirectory(appDirectory);
        var databasePath = Path.Combine(appDirectory, options.Value.DatabaseFileName);
        _connectionString = $"Data Source={databasePath}";
        EnsureDatabase();
    }

    /// <summary>
    /// 获取所有本地采集点。
    /// </summary>
    /// <returns>本地采集点集合。</returns>
    public Task<IReadOnlyCollection<LocalCollectionPointDto>> GetAllAsync()
    {
        var items = new List<LocalCollectionPointDto>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT LocalId, Code, Name, Protocol, Endpoint, IsEnabled, SyncStatus, UpdatedAt
            FROM LocalCollectionPoints
            ORDER BY UpdatedAt DESC;
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new LocalCollectionPointDto(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5) == 1,
                reader.GetString(6),
                DateTimeOffset.Parse(reader.GetString(7))));
        }

        return Task.FromResult<IReadOnlyCollection<LocalCollectionPointDto>>(items);
    }

    /// <summary>
    /// 保存本地采集点。
    /// </summary>
    /// <param name="point">需要保存的采集点。</param>
    /// <returns>保存后的采集点。</returns>
    public Task<LocalCollectionPointDto> UpsertAsync(LocalCollectionPointDto point)
    {
        var savedPoint = point with
        {
            LocalId = point.LocalId == Guid.Empty ? Guid.NewGuid() : point.LocalId,
            UpdatedAt = DateTimeOffset.UtcNow,
            SyncStatus = "待同步"
        };

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO LocalCollectionPoints (LocalId, Code, Name, Protocol, Endpoint, IsEnabled, SyncStatus, UpdatedAt)
            VALUES ($LocalId, $Code, $Name, $Protocol, $Endpoint, $IsEnabled, $SyncStatus, $UpdatedAt)
            ON CONFLICT(LocalId) DO UPDATE SET
                Code = excluded.Code,
                Name = excluded.Name,
                Protocol = excluded.Protocol,
                Endpoint = excluded.Endpoint,
                IsEnabled = excluded.IsEnabled,
                SyncStatus = excluded.SyncStatus,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$LocalId", savedPoint.LocalId.ToString());
        command.Parameters.AddWithValue("$Code", savedPoint.Code.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$Name", savedPoint.Name.Trim());
        command.Parameters.AddWithValue("$Protocol", savedPoint.Protocol.Trim());
        command.Parameters.AddWithValue("$Endpoint", savedPoint.Endpoint.Trim());
        command.Parameters.AddWithValue("$IsEnabled", savedPoint.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$SyncStatus", savedPoint.SyncStatus);
        command.Parameters.AddWithValue("$UpdatedAt", savedPoint.UpdatedAt.ToString("O"));
        command.ExecuteNonQuery();

        return Task.FromResult(savedPoint);
    }

    /// <summary>
    /// 将指定采集点标记为已同步。
    /// </summary>
    /// <param name="localIds">已同步的本地主键集合。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task MarkAsSyncedAsync(IEnumerable<Guid> localIds)
    {
        var idList = localIds.ToArray();
        if (idList.Length == 0)
        {
            return Task.CompletedTask;
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        foreach (var id in idList)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE LocalCollectionPoints
                SET SyncStatus = $SyncStatus, UpdatedAt = $UpdatedAt
                WHERE LocalId = $LocalId;
                """;
            command.Parameters.AddWithValue("$SyncStatus", "已同步");
            command.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$LocalId", id.ToString());
            command.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

    private void EnsureDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS LocalCollectionPoints
            (
                LocalId TEXT PRIMARY KEY,
                Code TEXT NOT NULL,
                Name TEXT NOT NULL,
                Protocol TEXT NOT NULL,
                Endpoint TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL,
                SyncStatus TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}
