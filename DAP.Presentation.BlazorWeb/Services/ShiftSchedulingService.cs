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
    public async Task<ShiftScheduleImportResultDto> ImportAsync(
        string fileName,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<ShiftScheduleRowDto> parsedRows;
        List<ShiftScheduleIssueDto> parseIssues;

        try
        {
            // 浏览器上传的 IBrowserFile 流不可 seek（ZipArchive 必须要求 seek），
            // 先缓冲进 MemoryStream 统一两条链路（页面直调 / HTTP 端点）的流形态。
            using MemoryStream buffered = new();
            await stream.CopyToAsync(buffered, cancellationToken);
            buffered.Position = 0;

            _parser.Parse(fileName, buffered, out parsedRows, out parseIssues);
        }
        catch (ShiftScheduleFormatException ex)
        {
            return new ShiftScheduleImportResultDto(
                false,
                fileName,
                [],
                new ShiftScheduleValidationDto(false, 0, 0, 0, 0, "暂无数据。",
                    [new ShiftScheduleIssueDto(0, ex.Message, "error")]),
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ClosedXML 在遇到加密或损坏文件时会抛出多种底层异常（类型随版本变化），
            // 这里统一转成可读中文提示，不把框架原始文案透传给界面。
            return new ShiftScheduleImportResultDto(
                false,
                fileName,
                [],
                new ShiftScheduleValidationDto(false, 0, 0, 0, 0, "暂无数据。",
                    [new ShiftScheduleIssueDto(0, "文件解析失败，请确认是有效的 .xlsx 或 .xls 文件。", "error")]),
                DateTimeOffset.UtcNow);
        }

        bool hasParseError = parseIssues.Any(item =>
            string.Equals(item.Severity, "error", StringComparison.OrdinalIgnoreCase));

        // 只按解析结果判定：出现 error 级解析问题不覆盖旧数据，
        // 避免「日期列读不出、班组编号越界」这类坏文件把可用排班清空。
        // 四班三运转轮转校验已按业务要求取消，导入不再拦截合法的真实台账。
        ShiftScheduleValidationDto validation = BuildSummary(parsedRows, parseIssues, isValid: !hasParseError);

        if (!hasParseError)
        {
            lock (_gate)
            {
                _rows = parsedRows;
                _lastImportedAt = DateTimeOffset.UtcNow;
                _lastFileName = fileName;
            }
        }

        return new ShiftScheduleImportResultDto(
            !hasParseError,
            fileName,
            !hasParseError ? parsedRows : [],
            validation,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// 汇总导入结果的基础统计（班组数、自然日数、完整覆盖天数）。
    ///
    /// 「完整覆盖」按「当天班次合计时长 ≥ 24 小时」判定，供状态展示；
    /// 不再执行四班三运转轮转校验。
    /// </summary>
    private static ShiftScheduleValidationDto BuildSummary(
        IReadOnlyCollection<ShiftScheduleRowDto> rows,
        IReadOnlyCollection<ShiftScheduleIssueDto> issues,
        bool isValid)
    {
        int dutyDays = rows.Select(item => item.WorkDate).Distinct().Count();
        int teams = rows.Select(item => item.TeamCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int coverageDays = rows
            .GroupBy(item => item.WorkDate)
            .Count(group => group.Sum(item => item.DurationHours) >= 24d);

        string summary = rows.Count == 0
            ? "暂无数据。"
            : isValid
                ? $"已导入 {rows.Count} 条班次记录，覆盖 {dutyDays} 个自然日。"
                : "解析存在问题，本次文件未入库。";

        return new ShiftScheduleValidationDto(isValid, rows.Count, dutyDays, teams, coverageDays, summary, issues);
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
