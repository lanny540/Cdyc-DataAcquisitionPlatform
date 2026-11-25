using DAP.Core.Shared.Contracts;

namespace DAP.Core.Domain.Services;

/// <summary>
/// 排班记录的分组与汇总工具。
///
/// 四班三运转轮转校验已按业务要求取消：真实台账由「自然日 + 夜班/白班/中班三列班组编号」
/// 直接给出值班事实，导入只做格式解析，不再拦截数据内容；
/// 本类仅保留日历分组与班组汇总两个查询辅助。
/// </summary>
public static class ShiftRotationValidator
{
    /// <summary>
    /// 标准班次工时，单位为小时。
    /// </summary>
    public const double ShiftHours = 8d;

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
                    group.OrderBy(item => item.StartTime).ThenBy(item => item.TeamCode, StringComparer.Ordinal)
                        .ToArray();

                string[] onDuty = dayRows
                    .Where(item => !item.IsRestDay)
                    .Select(item => item.TeamName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                string[] rest = dayRows
                    .Where(item => item.IsRestDay)
                    .Select(item => item.TeamName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                // 在岗班次数与要求班次数均为整数，直接整数比较，避免浮点相等判断。
                const int requiredShifts = (int)(24d / ShiftHours);
                bool complete = dayRows.Count(item => !item.IsRestDay) == requiredShifts;

                return new DailyShiftScheduleDto(group.Key, dayRows, onDuty, rest, complete);
            })
            .ToArray();
    }

    /// <summary>
    /// 汇总单个班组在区间内的排班，输出总工时与轮休统计。
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

        string teamName = teamRows.FirstOrDefault()?.TeamName ?? teamCode;

        int totalShifts = teamRows.Count(item => !item.IsRestDay);

        return new TeamShiftScheduleDto(
            teamCode,
            teamName,
            teamRows,
            totalShifts,
            Math.Round(teamRows.Sum(item => item.DurationHours), 2),
            teamRows.Count(item => item.IsRestDay));
    }
}
