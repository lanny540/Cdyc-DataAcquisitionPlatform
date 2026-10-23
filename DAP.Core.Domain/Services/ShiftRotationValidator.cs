using DAP.Core.Shared.Contracts;

namespace DAP.Core.Domain.Services;

/// <summary>
/// 四班三运转校验器。
///
/// 业务约定：每个班组每班工作 8 小时，班次结束后交给下一个班组，以此循环；
/// 全天由 3 个班次覆盖 24 小时，4 个班组轮转，保证每个班组都有轮休日。
/// </summary>
public static class ShiftRotationValidator
{
    /// <summary>
    /// 标准班次工时，单位为小时。
    /// </summary>
    public const double ShiftHours = 8d;

    /// <summary>
    /// 一个完整轮转周期内的班次数量，即四班三运转中的四个班组。
    /// </summary>
    public const int TeamsPerCycle = 4;

    /// <summary>
    /// 工时允许的误差，单位为小时。用于容忍 Excel 中人工录入的取整误差。
    /// </summary>
    private const double DurationToleranceHours = 1d / 60d;

    /// <summary>
    /// 允许的日期跨度上限，避免误传超大文件导致内存与计算放大。
    /// </summary>
    private const int MaxDateSpanDays = 400;

    /// <summary>
    /// 校验一组排班记录是否符合四班三运转要求。
    /// </summary>
    /// <param name="rows">解析后的班次记录。</param>
    /// <returns>校验结论。</returns>
    public static ShiftScheduleValidationDto Validate(IReadOnlyCollection<ShiftScheduleRowDto> rows)
    {
        List<ShiftScheduleIssueDto> issues = [];

        if (rows.Count == 0)
        {
            issues.Add(new ShiftScheduleIssueDto(0, "排班表中没有任何有效数据行。", "error"));
            return BuildResult(rows, issues);
        }

        ValidateDuplicates(rows, issues);
        ValidateDuration(rows, issues);
        IReadOnlyCollection<string> teams = ValidateCoverage(rows, issues);
        ValidateRestBalance(rows, teams, issues);

        return BuildResult(rows, issues);
    }

    /// <summary>
    /// 按自然日分组，供日历视图直接使用。
    /// </summary>
    /// <param name="rows">班次记录。</param>
    /// <returns>按日期升序排列的每日排班。</returns>
    public static IReadOnlyCollection<DailyShiftScheduleDto> GroupByDate(
        IReadOnlyCollection<ShiftScheduleRowDto> rows)
    {
        return rows
            .GroupBy(item => item.WorkDate)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                ShiftScheduleRowDto[] dayRows =
                    group.OrderBy(item => item.StartTime).ThenBy(item => item.TeamCode).ToArray();

                var onDuty = dayRows
                    .Where(item => !item.IsRestDay)
                    .Select(item => item.TeamName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                var rest = dayRows
                    .Where(item => item.IsRestDay)
                    .Select(item => item.TeamName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                var complete = dayRows.Count(item => !item.IsRestDay) == 24d / ShiftHours;

                return new DailyShiftScheduleDto(group.Key, dayRows, onDuty, rest, complete);
            })
            .ToArray();
    }

    /// <summary>
    /// 校验单个班组在区间内的排班，输出总工时与轮休统计。
    /// </summary>
    /// <param name="rows">全部班次记录。</param>
    /// <param name="teamCode">目标班组编码。</param>
    /// <returns>班组排班汇总。</returns>
    public static TeamShiftScheduleDto SummarizeTeam(
        IReadOnlyCollection<ShiftScheduleRowDto> rows,
        string teamCode)
    {
        ShiftScheduleRowDto[] teamRows = rows
            .Where(item => string.Equals(item.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.WorkDate)
            .ThenBy(item => item.StartTime)
            .ToArray();

        var teamName = teamRows.FirstOrDefault()?.TeamName ?? teamCode;

        var totalShifts = teamRows.Count(item => !item.IsRestDay);

        return new TeamShiftScheduleDto(
            teamCode,
            teamName,
            teamRows,
            totalShifts,
            Math.Round(teamRows.Sum(item => item.DurationHours), 2),
            teamRows.Count(item => item.IsRestDay));
    }

    private static void ValidateDuplicates(
        IReadOnlyCollection<ShiftScheduleRowDto> rows,
        List<ShiftScheduleIssueDto> issues)
    {
        foreach (var group in rows.GroupBy(item => new {item.WorkDate, item.TeamCode})
                     .Where(group => group.Count() > 1))
        {
            issues.Add(new ShiftScheduleIssueDto(
                0,
                $"{group.Key.WorkDate:yyyy-MM-dd} 的班组 {group.Key.TeamCode} 出现了 {group.Count()} 条记录，同一自然日同一班组只能有一条排班。",
                "error"));
        }
    }

    private static void ValidateDuration(
        IReadOnlyCollection<ShiftScheduleRowDto> rows,
        List<ShiftScheduleIssueDto> issues)
    {
        foreach (ShiftScheduleRowDto row in rows.Where(item => !item.IsRestDay))
        {
            if (Math.Abs(row.DurationHours - ShiftHours) > DurationToleranceHours)
            {
                issues.Add(new ShiftScheduleIssueDto(
                    0,
                    $"{row.WorkDate:yyyy-MM-dd} 班组 {row.TeamName} 的班次时长为 {row.DurationHours:0.##} 小时，四班三运转要求每班 {ShiftHours:0} 小时。",
                    "error"));
            }
        }
    }

    private static IReadOnlyCollection<string> ValidateCoverage(
        IReadOnlyCollection<ShiftScheduleRowDto> rows,
        List<ShiftScheduleIssueDto> issues)
    {
        DateOnly start = rows.Min(item => item.WorkDate);
        DateOnly end = rows.Max(item => item.WorkDate);

        if (end.DayNumber - start.DayNumber + 1 > MaxDateSpanDays)
        {
            issues.Add(new ShiftScheduleIssueDto(
                0,
                $"排班区间跨度 {end.DayNumber - start.DayNumber + 1} 天，超过单次导入上限 {MaxDateSpanDays} 天。",
                "error"));
        }

        foreach (IGrouping<DateOnly, ShiftScheduleRowDto> dayRows in rows.GroupBy(item => item.WorkDate))
        {
            ShiftScheduleRowDto[] dutyRows = dayRows.Where(item => !item.IsRestDay).ToArray();

            // 24 小时需要恰好三个 8 小时班次，多或少都不符合四班三运转。
            var requiredShifts = (int) (24d / ShiftHours);
            if (dutyRows.Length != requiredShifts)
            {
                issues.Add(new ShiftScheduleIssueDto(
                    0,
                    $"{dayRows.Key:yyyy-MM-dd} 有 {dutyRows.Length} 个在岗班次，四班三运转要求恰好 {requiredShifts} 个班次覆盖 24 小时。",
                    "error"));
                continue;
            }

            long expectedStartTicks = 0;
            foreach (ShiftScheduleRowDto row in dutyRows.OrderBy(item => item.StartTime))
            {
                if (row.StartTime.Ticks != expectedStartTicks)
                {
                    issues.Add(new ShiftScheduleIssueDto(
                        0,
                        $"{dayRows.Key:yyyy-MM-dd} 存在班次空档或重叠：{row.StartTime:HH\\:mm} 之前未连续覆盖到该时刻。",
                        "error"));
                    break;
                }

                expectedStartTicks = row.StartTime.Ticks +
                                     (long) Math.Round(row.DurationHours * TimeSpan.TicksPerHour);
            }

            if (expectedStartTicks < 24d * TimeSpan.TicksPerHour)
            {
                issues.Add(new ShiftScheduleIssueDto(
                    0,
                    $"{dayRows.Key:yyyy-MM-dd} 的排班未覆盖完整 24 小时，当日班次结束后仍有空档。",
                    "error"));
            }
        }

        return rows
            .Select(item => item.TeamCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 校验轮休均衡性：四班三运转要求每个班组轮休日数差异不超过 1 天。
    /// </summary>
    private static void ValidateRestBalance(
        IReadOnlyCollection<ShiftScheduleRowDto> rows,
        IReadOnlyCollection<string> teams,
        List<ShiftScheduleIssueDto> issues)
    {
        if (teams.Count < TeamsPerCycle)
        {
            issues.Add(new ShiftScheduleIssueDto(
                0,
                $"仅识别到 {teams.Count} 个班组，四班三运转需要 {TeamsPerCycle} 个班组轮转。",
                "warning"));
        }

        var dutyDays = rows.Select(item => item.WorkDate).Distinct().Count();
        if (dutyDays == 0)
        {
            return;
        }

        Dictionary<string, int> restCounts = teams.ToDictionary(
            team => team,
            team => rows.Count(item =>
                string.Equals(item.TeamCode, team, StringComparison.OrdinalIgnoreCase) && item.IsRestDay),
            StringComparer.OrdinalIgnoreCase);

        var expectedRest = dutyDays / Math.Max(1, teams.Count);
        var tolerance = expectedRest > 0 ? 1 : 0;

        foreach (KeyValuePair<string, int> pair in restCounts.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (Math.Abs(pair.Value - expectedRest) > tolerance)
            {
                issues.Add(new ShiftScheduleIssueDto(
                    0,
                    $"班组 {pair.Key} 轮休 {pair.Value} 天，与其他班组的轮休分配不均衡（期望约 {expectedRest} 天）。",
                    "warning"));
            }
        }
    }

    private static ShiftScheduleValidationDto BuildResult(
        IReadOnlyCollection<ShiftScheduleRowDto> rows,
        List<ShiftScheduleIssueDto> issues)
    {
        var dutyDays = rows.Select(item => item.WorkDate).Distinct().Count();
        var teams = rows.Select(item => item.TeamCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var coverageDays = GroupByDate(rows).Count(item => item.CoverageComplete);

        (string TeamName, string Code)[] teamNames = rows
            .GroupBy(item => item.TeamCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.First().TeamName, Code: group.Key))
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .ToArray();

        var restBalance = BuildRestBalanceDescription(teamNames, rows);

        return new ShiftScheduleValidationDto(
            !issues.Any(item => string.Equals(item.Severity, "error", StringComparison.OrdinalIgnoreCase)),
            rows.Count,
            dutyDays,
            teams,
            coverageDays,
            restBalance,
            issues);
    }

    private static string BuildRestBalanceDescription(
        IReadOnlyCollection<(string TeamName, string Code)> teams,
        IReadOnlyCollection<ShiftScheduleRowDto> rows)
    {
        if (teams.Count == 0)
        {
            return "暂无数据。";
        }

        return string.Join(
            "、",
            teams.Select(team =>
            {
                var rest = rows.Count(item =>
                    string.Equals(item.TeamCode, team.Code, StringComparison.OrdinalIgnoreCase) && item.IsRestDay);

                return $"{team.TeamName} 休 {rest} 天";
            }));
    }
}
