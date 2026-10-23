using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 四班三运转校验测试：确保排班导入会拦住不符合「每班 8 小时、连续覆盖、四班轮休」的数据。
/// </summary>
public class ShiftRotationValidatorTests
{
    [Fact]
    public void Validate_Should_Pass_For_Standard_FourShiftThreeRotation()
    {
        ShiftScheduleRowDto[] rows = BuildFourDaySchedule();

        ShiftScheduleValidationDto result = ShiftRotationValidator.Validate(rows);

        Assert.True(result.IsValid, string.Join(" ", result.Issues.Select(item => item.Message)));
        Assert.Equal(4, result.Teams);
        Assert.Equal(4, result.DutyDays);
        Assert.Equal(4, result.CoverageDays);
        Assert.Equal(16, result.TotalRows);
    }

    [Fact]
    public void Validate_Should_Fail_When_Shift_Is_Not_Eight_Hours()
    {
        ShiftScheduleRowDto[] rows = BuildFourDaySchedule()
            .Select(item => item is {IsRestDay: false} && item.StartTime == new TimeOnly(8, 0)
                ? item with {EndTime = new TimeOnly(15, 0), DurationHours = 7d}
                : item)
            .ToArray();

        ShiftScheduleValidationDto result = ShiftRotationValidator.Validate(rows);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("每班 8 小时", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Should_Fail_When_Day_Has_Gap_Between_Shifts()
    {
        // 第一个班次结束后留出 1 小时空档，导致 24 小时未被连续覆盖。
        ShiftScheduleRowDto[] rows = BuildFourDaySchedule()
            .Select(item => item is {IsRestDay: false} && item.StartTime == new TimeOnly(8, 0)
                ? item with {StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0)}
                : item)
            .ToArray();

        ShiftScheduleValidationDto result = ShiftRotationValidator.Validate(rows);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("空档或重叠", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Should_Fail_When_Team_Has_Two_Rows_On_Same_Day()
    {
        var date = new DateOnly(2026, 10, 20);
        ShiftScheduleRowDto duplicate =
            new(date, "TEAM-甲", "甲班", "中班", new TimeOnly(8, 0), new TimeOnly(16, 0), false, 8d);

        ShiftScheduleRowDto[] rows = [.. BuildFourDaySchedule(), duplicate];

        ShiftScheduleValidationDto result = ShiftRotationValidator.Validate(rows);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("只能有一条排班", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Should_Fail_When_Day_Has_Less_Than_Three_Shifts()
    {
        // 删掉丙班的全部记录后，当天只剩两个班次在岗。
        ShiftScheduleRowDto[] rows = BuildFourDaySchedule()
            .Where(item => item.TeamCode != "TEAM-丙")
            .ToArray();

        ShiftScheduleValidationDto result = ShiftRotationValidator.Validate(rows);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("恰好 3 个班次", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Should_Return_Error_For_Empty_Input()
    {
        ShiftScheduleValidationDto result = ShiftRotationValidator.Validate([]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("没有任何有效数据行", StringComparison.Ordinal));
    }

    [Fact]
    public void GroupByDate_Should_Separate_Duty_And_Rest_Teams()
    {
        // 首日由甲班轮休，构造数据时 restIndex 从 0 开始。
        var date = new DateOnly(2026, 10, 20);

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
    /// 构造 4 天 × 4 个班组的合规排班：
    /// 甲 00:00-08:00、乙 08:00-16:00、丙 16:00-24:00，丁轮休，逐日轮换休息组。
    /// </summary>
    private static ShiftScheduleRowDto[] BuildFourDaySchedule()
    {
        List<ShiftScheduleRowDto> rows = [];
        var start = new DateOnly(2026, 10, 20);

        string[] teamNames = ["甲班", "乙班", "丙班", "丁班"];
        var shifts = new (TimeOnly Start, TimeOnly End, string Name)[]
        {
            (new TimeOnly(0, 0), new TimeOnly(8, 0), "夜班"),
            (new TimeOnly(8, 0), new TimeOnly(16, 0), "早班"),
            (new TimeOnly(16, 0), new TimeOnly(0, 0), "中班")
        };

        for (var dayOffset = 0; dayOffset < 4; dayOffset++)
        {
            DateOnly date = start.AddDays(dayOffset);
            var restingIndex = dayOffset % 4;

            // 每天只有 3 个班次，当日在岗班组依次占用这三个槽位。
            for (var dutyIndex = 0; dutyIndex < 3; dutyIndex++)
            {
                var teamIndex = dutyIndex < restingIndex ? dutyIndex : dutyIndex + 1;
                var teamName = teamNames[teamIndex];
                (TimeOnly Start, TimeOnly End, string Name) shift = shifts[dutyIndex];

                rows.Add(new ShiftScheduleRowDto(date, TeamCode(teamName), teamName, shift.Name,
                    shift.Start, shift.End, false, 8d));
            }

            var restingName = teamNames[restingIndex];
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
