using System.Text.Json;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;
using Npgsql;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 使用 PostgreSQL 原子保存完整排班快照，多实例共享同一份数据。
/// </summary>
public sealed class PostgreSqlShiftSchedulingService(NpgsqlDataSource dataSource) : IShiftSchedulingService
{
    public async Task<ShiftScheduleImportResultDto> ImportAsync(
        string fileName,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        InMemoryShiftSchedulingService parser = new();
        ShiftScheduleImportResultDto result = await parser.ImportAsync(fileName, stream, cancellationToken);
        if (!result.Succeeded)
        {
            return result;
        }

        await using NpgsqlCommand command = dataSource.CreateCommand(
            """
            INSERT INTO shift_schedule_snapshot (id, rows_json, imported_at, file_name)
            VALUES (1, @rows::jsonb, @imported_at, @file_name)
            ON CONFLICT (id) DO UPDATE
            SET rows_json = EXCLUDED.rows_json,
                imported_at = EXCLUDED.imported_at,
                file_name = EXCLUDED.file_name
            """);
        command.Parameters.AddWithValue("rows", JsonSerializer.Serialize(result.Rows));
        command.Parameters.AddWithValue("imported_at", result.ImportedAt.ToUniversalTime());
        command.Parameters.AddWithValue("file_name", result.FileName);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return result;
    }

    public async Task<bool> ClearAsync()
    {
        await using NpgsqlCommand
            command = dataSource.CreateCommand("DELETE FROM shift_schedule_snapshot WHERE id = 1");
        await command.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<ShiftScheduleStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        ScheduleSnapshot snapshot = await ReadAsync(cancellationToken);
        ShiftScheduleRowDto[] rows = snapshot.Rows;
        ShiftTeamDto[] teams = rows.GroupBy(row => row.TeamCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ShiftTeamDto(group.Key, group.First().TeamName))
            .OrderBy(team => team.TeamName, StringComparer.Ordinal)
            .ToArray();

        return new ShiftScheduleStatusDto(
            rows.Length > 0,
            teams,
            rows.Length == 0 ? null : rows.Min(row => row.WorkDate),
            rows.Length == 0 ? null : rows.Max(row => row.WorkDate),
            rows.Length,
            snapshot.ImportedAt,
            snapshot.FileName);
    }

    public async Task<IReadOnlyCollection<DailyShiftScheduleDto>> GetCalendarAsync(
        DateOnly from,
        DateOnly to,
        string? teamCode = null,
        CancellationToken cancellationToken = default)
    {
        ShiftScheduleRowDto[] rows = (await ReadAsync(cancellationToken)).Rows
            .Where(row => row.WorkDate >= from && row.WorkDate <= to)
            .Where(row => string.IsNullOrWhiteSpace(teamCode) ||
                          string.Equals(row.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return ShiftRotationValidator.GroupByDate(rows);
    }

    public async Task<TeamShiftScheduleDto> GetTeamScheduleAsync(
        string teamCode,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        ShiftScheduleRowDto[] rows = (await ReadAsync(cancellationToken)).Rows
            .Where(row => row.WorkDate >= from && row.WorkDate <= to)
            .ToArray();
        return ShiftRotationValidator.SummarizeTeam(rows, teamCode);
    }

    public async Task<IReadOnlyCollection<ShiftScheduleRowDto>> GetAllRowsAsync(
        CancellationToken cancellationToken = default)
    {
        return (await ReadAsync(cancellationToken)).Rows;
    }

    private async Task<ScheduleSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "SELECT rows_json::text, imported_at, file_name FROM shift_schedule_snapshot WHERE id = 1");
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new ScheduleSnapshot([], null, null);
        }

        ShiftScheduleRowDto[] rows = JsonSerializer.Deserialize<ShiftScheduleRowDto[]>(reader.GetString(0))
                                     ?? throw new InvalidOperationException("排班数据格式无效，请检查服务端日志。");
        return new ScheduleSnapshot(rows, reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2));
    }

    private sealed record ScheduleSnapshot(
        ShiftScheduleRowDto[] Rows,
        DateTimeOffset? ImportedAt,
        string? FileName);
}
