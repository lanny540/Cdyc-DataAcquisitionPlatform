using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 定义班组排班在服务端对外提供的业务能力。
/// </summary>
public interface IShiftSchedulingService
{
    /// <summary>
    /// 导入 Excel 排班表，替换当前全部排班数据。
    /// </summary>
    /// <param name="fileName">文件名。</param>
    /// <param name="stream">Excel 内容流。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>导入结果。</returns>
    Task<ShiftScheduleImportResultDto> ImportAsync(
        string fileName,
        Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空已导入的排班数据。
    /// </summary>
    /// <returns>是否清空成功。</returns>
    Task<bool> ClearAsync();

    /// <summary>
    /// 获取排班表整体状态。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>排班状态。</returns>
    Task<ShiftScheduleStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定日期区间的每日排班，供日历视图使用。
    /// </summary>
    /// <param name="from">起始自然日。</param>
    /// <param name="to">结束自然日。</param>
    /// <param name="teamCode">班组过滤，为空表示全部班组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按日期排列的排班。</returns>
    Task<IReadOnlyCollection<DailyShiftScheduleDto>> GetCalendarAsync(
        DateOnly from,
        DateOnly to,
        string? teamCode = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定班组在日期区间内的排班明细。
    /// </summary>
    /// <param name="teamCode">班组编码。</param>
    /// <param name="from">起始自然日。</param>
    /// <param name="to">结束自然日。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>班组排班明细。</returns>
    Task<TeamShiftScheduleDto> GetTeamScheduleAsync(
        string teamCode,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取全部班组的排班明细，供横向对比与能耗页面复用。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全部班次记录。</returns>
    Task<IReadOnlyCollection<ShiftScheduleRowDto>> GetAllRowsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 提供基于内存存储的班组排班服务。
///
/// 排班表由 Excel 一次性导入，属于低频写入、高频读取的配置型数据，
/// 当前先用内存承载，后续接入数据库时只需替换本实现。
/// </summary>
public sealed class InMemoryShiftSchedulingService : IShiftSchedulingService
{
    private readonly ShiftScheduleExcelParser _parser = new();
    private readonly Lock _gate = new();

    private IReadOnlyCollection<ShiftScheduleRowDto> _rows = [];
    private DateTimeOffset? _lastImportedAt;
    private string? _lastFileName;

    /// <inheritdoc />
    public Task<ShiftScheduleImportResultDto> ImportAsync(
        string fileName,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<ShiftScheduleRowDto> parsedRows;
        List<ShiftScheduleIssueDto> parseIssues;

        try
        {
            _parser.Parse(fileName, stream, out parsedRows, out parseIssues);
        }
        catch (ShiftScheduleFormatException ex)
        {
            return Task.FromResult(new ShiftScheduleImportResultDto(
                false,
                fileName,
                [],
                new ShiftScheduleValidationDto(false, 0, 0, 0, 0, "暂无数据。",
                    [new ShiftScheduleIssueDto(0, ex.Message, "error")]),
                DateTimeOffset.UtcNow));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ClosedXML 在遇到加密或损坏文件时会抛出多种底层异常（类型随版本变化），
            // 这里统一转成可读中文提示，不把框架原始文案透传给界面。
            return Task.FromResult(new ShiftScheduleImportResultDto(
                false,
                fileName,
                [],
                new ShiftScheduleValidationDto(false, 0, 0, 0, 0, "暂无数据。",
                    [new ShiftScheduleIssueDto(0, "文件解析失败，请确认是有效的 .xlsx 或 .xls 文件。", "error")]),
                DateTimeOffset.UtcNow));
        }

        ShiftScheduleValidationDto validation = ShiftRotationValidator.Validate(parsedRows);

        List<ShiftScheduleIssueDto> allIssues = [.. parseIssues, .. validation.Issues];
        var hasParseError = parseIssues.Any(item =>
            string.Equals(item.Severity, "error", StringComparison.OrdinalIgnoreCase));

        // 只有全部通过校验才覆盖旧数据，避免坏文件把可用排班清空。
        if (validation.IsValid && !hasParseError)
        {
            lock (_gate)
            {
                _rows = parsedRows;
                _lastImportedAt = DateTimeOffset.UtcNow;
                _lastFileName = fileName;
            }

            validation = validation with {Issues = allIssues};
        }
        else
        {
            validation = validation with {Issues = allIssues};
        }

        return Task.FromResult(new ShiftScheduleImportResultDto(
            validation.IsValid && !hasParseError,
            fileName,
            validation.IsValid && !hasParseError ? parsedRows : [],
            validation,
            DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public Task<bool> ClearAsync()
    {
        lock (_gate)
        {
            _rows = [];
            _lastImportedAt = null;
            _lastFileName = null;
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<ShiftScheduleStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ShiftScheduleRowDto> rows = Snapshot();

        ShiftTeamDto[] teams = rows
            .GroupBy(item => item.TeamCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ShiftTeamDto(group.Key, group.First().TeamName))
            .OrderBy(item => item.TeamName, StringComparer.Ordinal)
            .ToArray();

        return Task.FromResult(new ShiftScheduleStatusDto(
            rows.Count > 0,
            teams,
            rows.Count == 0 ? null : rows.Min(item => item.WorkDate),
            rows.Count == 0 ? null : rows.Max(item => item.WorkDate),
            rows.Count,
            _lastImportedAt,
            _lastFileName));
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<DailyShiftScheduleDto>> GetCalendarAsync(
        DateOnly from,
        DateOnly to,
        string? teamCode = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<ShiftScheduleRowDto> rows = Snapshot()
            .Where(item => item.WorkDate >= from && item.WorkDate <= to);

        if (!string.IsNullOrWhiteSpace(teamCode))
        {
            rows = rows.Where(item => string.Equals(item.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult(ShiftRotationValidator.GroupByDate(rows.ToArray()));
    }

    /// <inheritdoc />
    public Task<TeamShiftScheduleDto> GetTeamScheduleAsync(
        string teamCode,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        ShiftScheduleRowDto[] rows = Snapshot()
            .Where(item => item.WorkDate >= from && item.WorkDate <= to)
            .ToArray();

        return Task.FromResult(ShiftRotationValidator.SummarizeTeam(rows, teamCode));
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<ShiftScheduleRowDto>> GetAllRowsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Snapshot());
    }

    private IReadOnlyCollection<ShiftScheduleRowDto> Snapshot()
    {
        lock (_gate)
        {
            return _rows;
        }
    }
}
