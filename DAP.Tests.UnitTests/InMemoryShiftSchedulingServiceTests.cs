using ClosedXML.Excel;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 排班服务测试。
///
/// 重点覆盖两类行为：
/// 1. **正常链路**——导入后状态、日历、班组汇总应与导入内容一致；
/// 2. **数据保护**——校验不通过的文件不得覆盖已有的可用排班，这是排班表最关键的约束。
/// </summary>
public class InMemoryShiftSchedulingServiceTests
{
    private static readonly DateOnly Day1 = new(2026, 10, 20);

    private readonly InMemoryShiftSchedulingService _service = new();

    #region 导入：正常链路

    [Fact]
    public async Task ImportAsync_With_Valid_Schedule_Should_Succeed_And_Report_Metadata()
    {
        using MemoryStream stream = BuildValidSchedule();

        ShiftScheduleImportResultDto result =
            await _service.ImportAsync("排班表.xlsx", stream);

        Assert.True(result.Succeeded);
        Assert.Equal("排班表.xlsx", result.FileName);
        Assert.Equal(16, result.Rows.Count);
        Assert.True(result.Validation.IsValid);
        Assert.NotEqual(default, result.ImportedAt);
    }

    [Fact]
    public async Task ImportAsync_Should_Replace_Previous_Data_Completely()
    {
        await _service.ImportAsync("第一批.xlsx", BuildValidSchedule());

        // 第二个文件只含 1 天 3 个班次，导入后旧数据应全部消失
        using MemoryStream second = BuildSchedule([new DateOnly(2026, 11, 1)]);
        ShiftScheduleImportResultDto result = await _service.ImportAsync("第二批.xlsx", second);

        Assert.True(result.Succeeded);

        IReadOnlyCollection<ShiftScheduleRowDto> rows = await _service.GetAllRowsAsync();
        Assert.Equal(4, rows.Count);
        Assert.All(rows, row => Assert.Equal(new DateOnly(2026, 11, 1), row.WorkDate));
    }

    #endregion

    #region 导入：数据保护（关键约束）

    [Fact]
    public async Task ImportAsync_With_Invalid_Schedule_Should_Keep_Existing_Data()
    {
        await _service.ImportAsync("正常表.xlsx", BuildValidSchedule());
        IReadOnlyCollection<ShiftScheduleRowDto> before = await _service.GetAllRowsAsync();

        // 构造不合法的排班：当天只有 2 个班次，未覆盖 24 小时
        using MemoryStream bad = BuildInvalidSchedule();
        ShiftScheduleImportResultDto result = await _service.ImportAsync("坏表.xlsx", bad);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Rows);

        IReadOnlyCollection<ShiftScheduleRowDto> after = await _service.GetAllRowsAsync();
        Assert.Equal(before.Count, after.Count);
    }

    [Fact]
    public async Task ImportAsync_With_Corrupted_File_Should_Return_Readable_Chinese_Error()
    {
        using var stream = new MemoryStream([0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE]);

        ShiftScheduleImportResultDto result = await _service.ImportAsync("坏文件.xlsx", stream);

        Assert.False(result.Succeeded);
        ShiftScheduleIssueDto issue = Assert.Single(result.Validation.Issues);
        Assert.Equal("error", issue.Severity);

        // 关键：不能把框架原始英文文案透传给界面
        Assert.Contains("文件解析失败", issue.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_With_Parse_Errors_Should_Merge_Them_Into_Validation()
    {
        using MemoryStream bad = BuildInvalidSchedule();

        ShiftScheduleImportResultDto result = await _service.ImportAsync("半成品.xlsx", bad);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Validation.Issues);
        Assert.Contains(result.Validation.Issues, issue => issue.Severity == "error");
    }

    [Fact]
    public async Task ImportAsync_When_Cancelled_Should_Throw_OperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        using MemoryStream stream = BuildValidSchedule();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.ImportAsync("排班表.xlsx", stream, cts.Token));
    }

    #endregion

    #region 状态查询

    [Fact]
    public async Task GetStatusAsync_On_Empty_Should_Report_No_Data()
    {
        ShiftScheduleStatusDto status = await _service.GetStatusAsync();

        Assert.False(status.HasData);
        Assert.Empty(status.Teams);
        Assert.Null(status.StartDate);
        Assert.Null(status.EndDate);
        Assert.Equal(0, status.TotalRows);
        Assert.Null(status.LastImportedAt);
        Assert.Null(status.LastFileName);
    }

    [Fact]
    public async Task GetStatusAsync_After_Import_Should_Report_Teams_And_Date_Range()
    {
        await _service.ImportAsync("排班表.xlsx", BuildValidSchedule());

        ShiftScheduleStatusDto status = await _service.GetStatusAsync();

        Assert.True(status.HasData);
        Assert.Equal(16, status.TotalRows);
        Assert.Equal(4, status.Teams.Count);
        Assert.Equal("排班表.xlsx", status.LastFileName);
        Assert.NotNull(status.LastImportedAt);

        // 班组应按名称排序，且编码已规范化（去掉尾部「班」字）
        Assert.Equal(
            new[] {"丁班", "丙班", "乙班", "甲班"},
            status.Teams.Select(team => team.TeamName).ToArray());
        Assert.All(status.Teams, team =>
        {
            Assert.StartsWith("TEAM-", team.TeamCode, StringComparison.Ordinal);
            Assert.DoesNotContain("班", team.TeamCode, StringComparison.Ordinal);
        });
    }

    #endregion

    #region 日历查询

    [Fact]
    public async Task GetCalendarAsync_Should_Only_Include_Rows_In_Range()
    {
        await _service.ImportAsync("排班表.xlsx", BuildValidSchedule());

        IReadOnlyCollection<DailyShiftScheduleDto> calendar =
            await _service.GetCalendarAsync(Day1, Day1.AddDays(1));

        Assert.Equal(2, calendar.Count);
        Assert.All(calendar, day => Assert.True(day.CoverageComplete));
    }

    [Fact]
    public async Task GetCalendarAsync_With_Team_Filter_Should_Return_Only_That_Team()
    {
        await _service.ImportAsync("排班表.xlsx", BuildValidSchedule());

        IReadOnlyCollection<DailyShiftScheduleDto> calendar =
            await _service.GetCalendarAsync(Day1, Day1.AddDays(3), "TEAM-甲");

        Assert.All(calendar, day =>
        {
            ShiftScheduleRowDto row = Assert.Single(day.Rows);
            Assert.Equal("TEAM-甲", row.TeamCode);
        });
    }

    [Fact]
    public async Task GetCalendarAsync_With_Unknown_Team_Should_Return_Empty()
    {
        await _service.ImportAsync("排班表.xlsx", BuildValidSchedule());

        IReadOnlyCollection<DailyShiftScheduleDto> calendar =
            await _service.GetCalendarAsync(Day1, Day1.AddDays(3), "TEAM-不存在");

        Assert.Empty(calendar);
    }

    [Fact]
    public async Task GetCalendarAsync_On_Empty_Service_Should_Return_Empty()
    {
        IReadOnlyCollection<DailyShiftScheduleDto> calendar =
            await _service.GetCalendarAsync(Day1, Day1.AddDays(3));

        Assert.Empty(calendar);
    }

    #endregion

    #region 班组汇总

    [Fact]
    public async Task GetTeamScheduleAsync_Should_Summarize_Shifts_Hours_And_Rest()
    {
        await _service.ImportAsync("排班表.xlsx", BuildValidSchedule());

        // 4 天轮转中，甲班轮休 1 天
        TeamShiftScheduleDto summary =
            await _service.GetTeamScheduleAsync("TEAM-甲", Day1, Day1.AddDays(3));

        Assert.Equal("甲班", summary.TeamName);
        Assert.Equal(3, summary.TotalShifts);
        Assert.Equal(24d, summary.TotalHours);
        Assert.Equal(1, summary.RestDays);
    }

    [Fact]
    public async Task GetTeamScheduleAsync_With_Unknown_Team_Should_Return_Empty_Summary()
    {
        await _service.ImportAsync("排班表.xlsx", BuildValidSchedule());

        TeamShiftScheduleDto summary =
            await _service.GetTeamScheduleAsync("TEAM-不存在", Day1, Day1.AddDays(3));

        Assert.Equal(0, summary.TotalShifts);
        Assert.Equal(0, summary.TotalHours);
        Assert.Empty(summary.Rows);
    }

    #endregion

    #region 清空

    [Fact]
    public async Task ClearAsync_Should_Reset_All_State()
    {
        await _service.ImportAsync("排班表.xlsx", BuildValidSchedule());

        var cleared = await _service.ClearAsync();

        Assert.True(cleared);

        ShiftScheduleStatusDto status = await _service.GetStatusAsync();
        Assert.False(status.HasData);
        Assert.Equal(0, status.TotalRows);
        Assert.Null(status.LastFileName);
        Assert.Null(status.LastImportedAt);
        Assert.Empty(await _service.GetAllRowsAsync());
    }

    [Fact]
    public async Task ClearAsync_On_Empty_Service_Should_Still_Return_True()
    {
        Assert.True(await _service.ClearAsync());
    }

    [Fact]
    public async Task ClearAsync_Should_Not_Affect_Later_Imports()
    {
        await _service.ImportAsync("排班表.xlsx", BuildValidSchedule());
        await _service.ClearAsync();

        using MemoryStream again = BuildValidSchedule();
        ShiftScheduleImportResultDto result = await _service.ImportAsync("再次导入.xlsx", again);

        Assert.True(result.Succeeded);
        Assert.Equal(16, (await _service.GetAllRowsAsync()).Count);
    }

    #endregion

    #region 测试数据构造

    /// <summary>
    /// 构造 4 天合规排班（每天 3 个班次覆盖 24 小时 + 1 个班组轮休），共 16 行。
    /// </summary>
    private static MemoryStream BuildValidSchedule()
    {
        return BuildSchedule([Day1, Day1.AddDays(1), Day1.AddDays(2), Day1.AddDays(3)]);
    }

    private static MemoryStream BuildSchedule(DateOnly[] dates)
    {
        var stream = new MemoryStream();

        using (var workbook = new XLWorkbook())
        {
            IXLWorksheet worksheet = workbook.Worksheets.Add("排班表");
            worksheet.Cell(1, 1).Value = "自然日";
            worksheet.Cell(1, 2).Value = "班组名称";
            worksheet.Cell(1, 3).Value = "上班时间";
            worksheet.Cell(1, 4).Value = "下班时间";

            string[] teamNames = ["甲班", "乙班", "丙班", "丁班"];
            var shifts = new (TimeOnly Start, TimeOnly End)[]
            {
                (new TimeOnly(0, 0), new TimeOnly(8, 0)),
                (new TimeOnly(8, 0), new TimeOnly(16, 0)),
                (new TimeOnly(16, 0), new TimeOnly(0, 0))
            };

            var row = 2;
            for (var dayIndex = 0; dayIndex < dates.Length; dayIndex++)
            {
                DateOnly date = dates[dayIndex];
                var restingIndex = dayIndex % 4;

                for (var dutyIndex = 0; dutyIndex < 3; dutyIndex++)
                {
                    var teamIndex = dutyIndex < restingIndex ? dutyIndex : dutyIndex + 1;
                    (TimeOnly Start, TimeOnly End) shift = shifts[dutyIndex];

                    worksheet.Cell(row, 1).Value = date.ToDateTime(new TimeOnly(0, 0));
                    worksheet.Cell(row, 2).Value = teamNames[teamIndex];
                    worksheet.Cell(row, 3).Value = shift.Start.ToTimeSpan();
                    worksheet.Cell(row, 4).Value = shift.End.ToTimeSpan();
                    row++;
                }

                worksheet.Cell(row, 1).Value = date.ToDateTime(new TimeOnly(0, 0));
                worksheet.Cell(row, 2).Value = teamNames[restingIndex];
                worksheet.Cell(row, 3).Value = "休息";
                worksheet.Cell(row, 4).Value = "休息";
                row++;
            }

            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// 构造不合法的排班：某天只有 2 个班次，未覆盖完整 24 小时。
    /// </summary>
    private static MemoryStream BuildInvalidSchedule()
    {
        var stream = new MemoryStream();

        using (var workbook = new XLWorkbook())
        {
            IXLWorksheet worksheet = workbook.Worksheets.Add("排班表");
            worksheet.Cell(1, 1).Value = "自然日";
            worksheet.Cell(1, 2).Value = "班组名称";
            worksheet.Cell(1, 3).Value = "上班时间";
            worksheet.Cell(1, 4).Value = "下班时间";

            worksheet.Cell(2, 1).Value = Day1.ToDateTime(new TimeOnly(0, 0));
            worksheet.Cell(2, 2).Value = "甲班";
            worksheet.Cell(2, 3).Value = new TimeSpan(0, 0, 0);
            worksheet.Cell(2, 4).Value = new TimeSpan(8, 0, 0);

            worksheet.Cell(3, 1).Value = Day1.ToDateTime(new TimeOnly(0, 0));
            worksheet.Cell(3, 2).Value = "乙班";
            worksheet.Cell(3, 3).Value = new TimeSpan(8, 0, 0);
            worksheet.Cell(3, 4).Value = new TimeSpan(16, 0, 0);

            // 缺 16:00-24:00 班次，当日覆盖不完整
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }

    #endregion
}
