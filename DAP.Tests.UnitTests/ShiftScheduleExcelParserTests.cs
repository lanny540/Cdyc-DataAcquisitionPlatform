using ClosedXML.Excel;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 排班表 Excel 解析测试。
///
/// 这些用例覆盖真实台账里最容易出问题的地方：表头前置说明行、列顺序调整、
/// 跨天班次、以及写库工具把时刻写成 TimeSpan 型单元格的情况。
/// </summary>
public class ShiftScheduleExcelParserTests
{
    private readonly ShiftScheduleExcelParser _parser = new();

    [Fact]
    public void Parse_Should_Accept_Header_Preceded_By_Title_Row()
    {
        using MemoryStream stream = BuildWorkbook((worksheet, culture) =>
        {
            // 现场台账常在表头上方放一行标题
            worksheet.Cell(1, 1).Value = "XX 选矿厂四班三运转排班表";
            WriteHeader(worksheet, 3, false);
            WriteDay(worksheet, 4, "2026-10-20", "甲班", 0, 8);
            WriteDay(worksheet, 5, "2026-10-20", "乙班", 8, 16);
            WriteDay(worksheet, 6, "2026-10-20", "丙班", 16, 0);
            WriteDay(worksheet, 7, "2026-10-20", "丁班", -1, -1);
        });

        _parser.Parse("test.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(issues);
        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public void Parse_Should_Accept_Reordered_Columns()
    {
        using MemoryStream stream = BuildWorkbook((worksheet, culture) =>
        {
            // 列顺序与关键字匹配，不影响解析
            worksheet.Cell(1, 1).Value = "班组名称";
            worksheet.Cell(1, 2).Value = "下班时间";
            worksheet.Cell(1, 3).Value = "自然日";
            worksheet.Cell(1, 4).Value = "上班时间";

            worksheet.Cell(2, 3).Value = new DateTime(2026, 10, 20);
            worksheet.Cell(2, 1).Value = "甲班";
            worksheet.Cell(2, 4).Value = new TimeSpan(8, 0, 0);
            worksheet.Cell(2, 2).Value = new TimeSpan(16, 0, 0);
        });

        _parser.Parse("test.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(issues);
        ShiftScheduleRowDto row = Assert.Single(rows);
        Assert.Equal(new DateOnly(2026, 10, 20), row.WorkDate);
        Assert.Equal("甲班", row.TeamName);
        Assert.Equal(new TimeOnly(8, 0), row.StartTime);
        Assert.Equal(new TimeOnly(16, 0), row.EndTime);
        Assert.Equal(8d, row.DurationHours);
    }

    [Fact]
    public void Parse_Should_Read_TimeSpan_Typed_Time_Cells_As_Clock_Time()
    {
        // 部分写库工具（openpyxl 等）会把时刻写成 TimeSpan 型单元格，
        // ClosedXML 的 TimeSpan 转换可能带回 Excel 日期序列号，导致解析失败。
        using MemoryStream stream = BuildWorkbook((worksheet, culture) =>
        {
            WriteHeader(worksheet, 1, false);
            WriteDay(worksheet, 2, "2026-10-20", "甲班", 8, 16);
        });

        _parser.Parse("test.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(issues);
        ShiftScheduleRowDto row = Assert.Single(rows);
        Assert.Equal(new TimeOnly(8, 0), row.StartTime);
        Assert.Equal(new TimeOnly(16, 0), row.EndTime);
        Assert.Equal(8d, row.DurationHours);
    }

    [Fact]
    public void Parse_Should_Treat_Rest_Marker_As_Rest_Day()
    {
        using MemoryStream stream = BuildWorkbook((worksheet, culture) =>
        {
            WriteHeader(worksheet, 1, false);
            WriteDay(worksheet, 2, "2026-10-20", "丁班", -1, -1);
        });

        _parser.Parse("test.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(issues);
        Assert.True(Assert.Single(rows).IsRestDay);
    }

    [Fact]
    public void Parse_Should_Accept_Text_Times()
    {
        using MemoryStream stream = BuildWorkbook((worksheet, culture) =>
        {
            worksheet.Cell(1, 1).Value = "自然日";
            worksheet.Cell(1, 2).Value = "班组名称";
            worksheet.Cell(1, 3).Value = "上班时间";
            worksheet.Cell(1, 4).Value = "下班时间";

            worksheet.Cell(2, 1).Value = "2026/10/20";
            worksheet.Cell(2, 2).Value = "甲班";
            worksheet.Cell(2, 3).Value = "8:00";
            worksheet.Cell(2, 4).Value = "16:00";
        });

        _parser.Parse("test.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(issues);
        ShiftScheduleRowDto row = Assert.Single(rows);
        Assert.Equal(new DateOnly(2026, 10, 20), row.WorkDate);
        Assert.Equal(new TimeOnly(8, 0), row.StartTime);
    }

    [Fact]
    public void Parse_Should_Skip_Blank_Rows_Without_Reporting_Errors()
    {
        using MemoryStream stream = BuildWorkbook((worksheet, culture) =>
        {
            WriteHeader(worksheet, 1, false);
            WriteDay(worksheet, 2, "2026-10-20", "甲班", 0, 8);
            // 第 3 行留空，模拟台账里的空行
            WriteDay(worksheet, 4, "2026-10-21", "甲班", 0, 8);
        });

        _parser.Parse("test.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(issues);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Parse_Should_Report_Error_When_Missing_Required_Columns()
    {
        using MemoryStream stream = BuildWorkbook((worksheet, culture) =>
        {
            worksheet.Cell(1, 1).Value = "自然日";
            worksheet.Cell(1, 2).Value = "班组名称";
            // 缺少上班时间与下班时间两列
        });

        _parser.Parse("test.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(rows);
        Assert.Contains(issues, issue =>
            issue.Severity == "error" && issue.Message.Contains("未能识别表头", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_Should_Report_Error_For_Unparsable_Date()
    {
        using MemoryStream stream = BuildWorkbook((worksheet, culture) =>
        {
            WriteHeader(worksheet, 1, false);
            worksheet.Cell(2, 1).Value = "不是日期";
            worksheet.Cell(2, 2).Value = "甲班";
            worksheet.Cell(2, 3).Value = new TimeSpan(0, 8, 0);
            worksheet.Cell(2, 4).Value = new TimeSpan(16, 0, 0);
        });

        _parser.Parse("test.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(rows);
        Assert.Contains(issues,
            issue => issue.Severity == "error" && issue.Message.Contains("自然日无法识别", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("甲班", "TEAM-甲")]
    [InlineData("甲", "TEAM-甲")]
    [InlineData(" 乙班 ", "TEAM-乙")]
    [InlineData("一班", "TEAM-一")]
    public void NormalizeTeamCode_Should_Strip_Trailing_Ban_Character(string teamName, string expected)
    {
        // 编码规则必须与能耗服务的默认班组编码（TEAM-甲等）保持一致，
        // 否则排班页与能耗页会指向两个不同的班组。
        Assert.Equal(expected, ShiftScheduleExcelParser.NormalizeTeamCode(teamName));
    }

    private static MemoryStream BuildWorkbook(Action<IXLWorksheet, System.Globalization.CultureInfo> fill)
    {
        var stream = new MemoryStream();

        using (var workbook = new XLWorkbook())
        {
            IXLWorksheet worksheet = workbook.Worksheets.Add("排班表");
            fill(worksheet, System.Globalization.CultureInfo.InvariantCulture);

            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteHeader(IXLWorksheet worksheet, int row, bool reorderColumns)
    {
        worksheet.Cell(row, 1).Value = "自然日";
        worksheet.Cell(row, 2).Value = "班组名称";
        worksheet.Cell(row, 3).Value = "上班时间";
        worksheet.Cell(row, 4).Value = "下班时间";
    }

    /// <summary>
    /// 写入一条班次记录，startHour/endHour 为 -1 时表示轮休。
    /// </summary>
    private static void WriteDay(IXLWorksheet worksheet, int row, string date, string team, int startHour, int endHour)
    {
        if (DateOnly.TryParse(date, out DateOnly parsed))
        {
            worksheet.Cell(row, 1).Value = parsed.ToDateTime(new TimeOnly(0, 0));
        }

        worksheet.Cell(row, 2).Value = team;

        if (startHour < 0)
        {
            worksheet.Cell(row, 3).Value = "休息";
            worksheet.Cell(row, 4).Value = "休息";
            return;
        }

        worksheet.Cell(row, 3).Value = new TimeSpan(startHour, 0, 0);
        worksheet.Cell(row, 4).Value = endHour == 0
            ? "00:00"
            : new TimeSpan(endHour, 0, 0);
    }
}
