namespace DAP.Core.Shared.Contracts;

/// <summary>
/// 表示排班表中的一条班次记录，对应 Excel 中的一行数据。
/// </summary>
/// <param name="WorkDate">自然日，仅取日期部分。</param>
/// <param name="TeamCode">班组编码。</param>
/// <param name="TeamName">班组名称。</param>
/// <param name="ShiftName">班次名称，例如早班、中班、晚班、休息。</param>
/// <param name="StartTime">上班时间。</param>
/// <param name="EndTime">下班时间。</param>
/// <param name="IsRestDay">是否为本班组的轮休日。</param>
/// <param name="DurationHours">本班次工时，单位为小时。</param>
public sealed record ShiftScheduleRowDto(
    DateOnly WorkDate,
    string TeamCode,
    string TeamName,
    string ShiftName,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsRestDay,
    double DurationHours);

/// <summary>
/// 表示单个班组在指定日期区间的排班明细。
/// </summary>
/// <param name="TeamCode">班组编码。</param>
/// <param name="TeamName">班组名称。</param>
/// <param name="Rows">该班组在区间内的全部班次记录。</param>
/// <param name="TotalShifts">总出勤班次数。</param>
/// <param name="TotalHours">总出勤工时。</param>
/// <param name="RestDays">轮休日数。</param>
public sealed record TeamShiftScheduleDto(
    string TeamCode,
    string TeamName,
    IReadOnlyCollection<ShiftScheduleRowDto> Rows,
    int TotalShifts,
    double TotalHours,
    int RestDays);

/// <summary>
/// 表示某一自然日全厂当天的完整排班情况。
/// </summary>
/// <param name="WorkDate">自然日。</param>
/// <param name="Rows">当天全部班次的记录。</param>
/// <param name="OnDutyTeams">当天在岗班组名称。</param>
/// <param name="RestTeams">当天轮休班组名称。</param>
/// <param name="CoverageComplete">是否满足四班三运转的完整覆盖要求。</param>
public sealed record DailyShiftScheduleDto(
    DateOnly WorkDate,
    IReadOnlyCollection<ShiftScheduleRowDto> Rows,
    IReadOnlyCollection<string> OnDutyTeams,
    IReadOnlyCollection<string> RestTeams,
    bool CoverageComplete);

/// <summary>
/// 表示导入过程中发现的一条校验问题。
/// </summary>
/// <param name="RowNumber">Excel 中的行号，1 表示表头所在行。</param>
/// <param name="Message">问题描述。</param>
/// <param name="Severity">严重级别，取值为 error 或 warning。</param>
public sealed record ShiftScheduleIssueDto(int RowNumber, string Message, string Severity);

/// <summary>
/// 表示四班三运转的整体校验结论。
/// </summary>
/// <param name="IsValid">是否完全通过校验，没有 error 级问题。</param>
/// <param name="TotalRows">参与校验的记录总数。</param>
/// <param name="DutyDays">已排班的自然日天数。</param>
/// <param name="Teams">识别到的班组数量。</param>
/// <param name="CoverageDays">覆盖完整的天数。</param>
/// <param name="RestBalanceDescription">轮休均衡性的文字结论。</param>
/// <param name="Issues">校验问题明细。</param>
public sealed record ShiftScheduleValidationDto(
    bool IsValid,
    int TotalRows,
    int DutyDays,
    int Teams,
    int CoverageDays,
    string RestBalanceDescription,
    IReadOnlyCollection<ShiftScheduleIssueDto> Issues);

/// <summary>
/// 表示班组排班表导入结果。
/// </summary>
/// <param name="Succeeded">导入并通过校验是否成功。</param>
/// <param name="FileName">上传的文件名。</param>
/// <param name="Rows">解析成功且已入库的班次记录。</param>
/// <param name="Validation">四班三运转校验结论。</param>
/// <param name="ImportedAt">导入完成时间。</param>
public sealed record ShiftScheduleImportResultDto(
    bool Succeeded,
    string FileName,
    IReadOnlyCollection<ShiftScheduleRowDto> Rows,
    ShiftScheduleValidationDto Validation,
    DateTimeOffset ImportedAt);

/// <summary>
/// 表示排班表整体状态。
/// </summary>
/// <param name="HasData">是否已导入过排班数据。</param>
/// <param name="Teams">全部班组。</param>
/// <param name="StartDate">已导入的最早自然日。</param>
/// <param name="EndDate">已导入的最晚自然日。</param>
/// <param name="TotalRows">已入库记录总数。</param>
/// <param name="LastImportedAt">最近一次导入时间。</param>
/// <param name="LastFileName">最近一次导入的文件名。</param>
public sealed record ShiftScheduleStatusDto(
    bool HasData,
    IReadOnlyCollection<ShiftTeamDto> Teams,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int TotalRows,
    DateTimeOffset? LastImportedAt,
    string? LastFileName);

/// <summary>
/// 表示一个班组的静态信息。
/// </summary>
/// <param name="TeamCode">班组编码。</param>
/// <param name="TeamName">班组名称。</param>
public sealed record ShiftTeamDto(string TeamCode, string TeamName);
