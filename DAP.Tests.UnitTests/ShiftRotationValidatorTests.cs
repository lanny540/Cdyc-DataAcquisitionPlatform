using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 排班分组与汇总工具测试。
///
/// 四班三运转轮转校验已按业务要求取消，这里只覆盖日历分组与班组汇总的查询行为。
/// </summary>
public class ShiftRotationValidatorTests
{
    [Fact]
    public void GroupByDate_Should_Separate_Duty_And_Rest_Teams()
    {
        // 首日由甲班轮休，构造数据时 restIndex 从 0 开始。
        DateOnly date = new(2026, 10, 20);

        IReadOnlyCollection<DailyShiftScheduleDto> calendar =
            ShiftRotationValidator.GroupByDate(BuildFourDaySchedule());

        DailyShiftScheduleDto day = calendar.Single(item => item.WorkDate == date);

        Assert.True(day.CoverageComplete);
        Assert.Equal(3, day.OnDutyTeams.Count);
        Assert.Equal(["甲班"], day.RestTeams);
        Assert.DoesNotContain("甲班", day.OnDutyTeams);
    }

    [Fact]
    public void SummarizeTeam_Should_Exclude_Rest_Days_From_Workload()
    {
        TeamShiftScheduleDto summary =
            ShiftRotationValidator.SummarizeTeam(BuildFourDaySchedule(), "TEAM-甲");

        // 轮休日不计工时：甲班 4 天中出勤 3 天，合计 24 小时。
        Assert.Equal(3, summary.TotalShifts);
        Assert.Equal(24d, summary.TotalHours);
        Assert.Equal(1, summary.RestDays);
    }

    /// <summary>
    /// 构造 4 天 × 4 个班组的排班：
    /// 甲 00:00-08:00、乙 08:00-16:00、丙 16:00-24:00，丁轮休，逐日轮换休息组。
    /// </summary>
    private static ShiftScheduleRowDto[] BuildFourDaySchedule()
    {
        List<ShiftScheduleRowDto> rows = [];
        DateOnly start = new(2026, 10, 20);

        string[] teamNames = ["甲班", "乙班", "丙班", "丁班"];
        (TimeOnly Start, TimeOnly End, string Name)[] shifts = new (TimeOnly Start, TimeOnly End, string Name)[]
        {
            (new TimeOnly(0, 0), new TimeOnly(8, 0), "夜班"), (new TimeOnly(8, 0), new TimeOnly(16, 0), "早班"),
            (new TimeOnly(16, 0), new TimeOnly(0, 0), "中班")
        };

        for (int dayOffset = 0; dayOffset < 4; dayOffset++)
        {
            DateOnly date = start.AddDays(dayOffset);
            int restingIndex = dayOffset % 4;

            // 每天只有 3 个班次，当日在岗班组依次占用这三个槽位。
            for (int dutyIndex = 0; dutyIndex < 3; dutyIndex++)
            {
                int teamIndex = dutyIndex < restingIndex ? dutyIndex : dutyIndex + 1;
                string teamName = teamNames[teamIndex];
                (TimeOnly Start, TimeOnly End, string Name) shift = shifts[dutyIndex];

                rows.Add(new ShiftScheduleRowDto(date, TeamCode(teamName), teamName, shift.Name,
                    shift.Start, shift.End, false, 8d));
            }

            string restingName = teamNames[restingIndex];
            rows.Add(new ShiftScheduleRowDto(date, TeamCode(restingName), restingName, "休息",
                TimeOnly.MinValue, TimeOnly.MinValue, true, 0d));
        }

        return rows.ToArray();
    }

    /// <summary>
    /// 班组编码去掉「班」字后作为标识，与服务端 ShiftScheduleExcelParser.NormalizeTeamCode 一致。
    /// </summary>
    private static string TeamCode(string teamName)
    {
        return $"TEAM-{teamName.TrimEnd('班')}";
    }
}
