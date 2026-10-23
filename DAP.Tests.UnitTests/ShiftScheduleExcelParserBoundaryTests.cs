using ClosedXML.Excel;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 排班表解析的边界与防御性用例。
///
/// 这些场景现场很容易遇到：文件超限、表格是空的、某一列没填、
/// 时间写成五花八门的格式。解析器必须在这些情况下给出可读的中文错误，
/// 而不是抛异常或静默产生空数据。
/// </summary>
public class ShiftScheduleExcelParserBoundaryTests
{
    private readonly ShiftScheduleExcelParser _parser = new();

    [Fact]
    public void Parse_With_Oversized_Stream_Should_Report_Size_Limit()
    {
        // 用可 seek 的长流模拟超限文件，避免真的分配上百 MB 内存
        using var stream = new LengthOnlyStream(64L * 1024 * 1024);

        var parsed = _parser.Parse("大文件.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Equal(0, parsed);
        Assert.Empty(rows);
        ShiftScheduleIssueDto issue = Assert.Single(issues);
        Assert.Equal("error", issue.Severity);
        Assert.Contains("超过", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_With_Empty_Worksheet_Should_Report_No_Content()
    {
        using var stream = new MemoryStream();

        using (var workbook = new XLWorkbook())
        {
            workbook.Worksheets.Add("空表");
            workbook.SaveAs(stream);
        }

        stream.Position = 0;

        _parser.Parse("空表.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(rows);
        Assert.Contains(issues, issue =>
            issue.Severity == "error" && issue.Message.Contains("为空", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_With_Blank_Team_Name_Should_Report_Error_And_Continue()
    {
        using MemoryStream stream = BuildWorkbook(worksheet =>
        {
            WriteHeader(worksheet, 1);
            worksheet.Cell(2, 1).Value = new DateTime(2026, 10, 20);
            worksheet.Cell(2, 2).Value = "   ";
            worksheet.Cell(2, 3).Value = new TimeSpan(0, 0, 0);
            worksheet.Cell(2, 4).Value = new TimeSpan(8, 0, 0);
        });

        _parser.Parse("空班组.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(rows);
        Assert.Contains(issues, issue => issue.Message.Contains("班组名称为空", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_With_Unparsable_Start_Time_Should_Report_Error()
    {
        using MemoryStream stream = BuildWorkbook(worksheet =>
        {
            WriteHeader(worksheet, 1);
            worksheet.Cell(2, 1).Value = new DateTime(2026, 10, 20);
            worksheet.Cell(2, 2).Value = "甲班";
            worksheet.Cell(2, 3).Value = "上班";
            worksheet.Cell(2, 4).Value = new TimeSpan(8, 0, 0);
        });

        _parser.Parse("坏时间.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(rows);
        Assert.Contains(issues, issue => issue.Message.Contains("上班时间无法识别", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_With_Unparsable_End_Time_Should_Mention_Not_To_Use_24_Colon_00()
    {
        // 用户很容易把跨天班次写成 24:00，需要在错误提示中明确纠正
        using MemoryStream stream = BuildWorkbook(worksheet =>
        {
            WriteHeader(worksheet, 1);
            worksheet.Cell(2, 1).Value = new DateTime(2026, 10, 20);
            worksheet.Cell(2, 2).Value = "甲班";
            worksheet.Cell(2, 3).Value = new TimeSpan(16, 0, 0);
            worksheet.Cell(2, 4).Value = "24:00";
        });

        _parser.Parse("跨天.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(rows);
        ShiftScheduleIssueDto issue = Assert.Single(issues);
        Assert.Contains("下班时间无法识别", issue.Message, StringComparison.Ordinal);
        Assert.Contains("00:00", issue.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("8:00", 8, 0)]
    [InlineData("08:00", 8, 0)]
    [InlineData("8:00:00", 8, 0)]
    [InlineData("08时30分", 8, 30)]
    public void Parse_Should_Accept_Common_Time_Formats(string text, int hour, int minute)
    {
        using MemoryStream stream = BuildWorkbook(worksheet =>
        {
            WriteHeader(worksheet, 1);
            worksheet.Cell(2, 1).Value = new DateTime(2026, 10, 20);
            worksheet.Cell(2, 2).Value = "甲班";
            worksheet.Cell(2, 3).Value = text;
            worksheet.Cell(2, 4).Value = new TimeSpan(16, 0, 0);
        });

        _parser.Parse("多格式.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(issues);
        ShiftScheduleRowDto row = Assert.Single(rows);
        Assert.Equal(new TimeOnly(hour, minute), row.StartTime);
    }

    [Fact]
    public void Parse_Should_Read_Excel_Time_Fraction_As_Clock_Time()
    {
        // Excel 内部用「一天的小数比例」表示纯时间，解析器需换算回时刻
        using MemoryStream stream = BuildWorkbook(worksheet =>
        {
            WriteHeader(worksheet, 1);
            worksheet.Cell(2, 1).Value = new DateTime(2026, 10, 20);
            worksheet.Cell(2, 2).Value = "甲班";
            worksheet.Cell(2, 3).Value = 8.0 / 24.0; // 0.3333 = 08:00
            worksheet.Cell(2, 4).Value = 16.0 / 24.0; // 0.6667 = 16:00
        });

        _parser.Parse("小数时间.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(issues);
        ShiftScheduleRowDto row = Assert.Single(rows);
        Assert.Equal(new TimeOnly(8, 0), row.StartTime);
        Assert.Equal(new TimeOnly(16, 0), row.EndTime);
    }

    [Fact]
    public void Parse_Should_Accept_Headers_With_Extra_Spaces_And_Synonyms()
    {
        using MemoryStream stream = BuildWorkbook(worksheet =>
        {
            // 现场台账表头常有空格或同义写法
            worksheet.Cell(1, 1).Value = " 自 然 日 ";
            worksheet.Cell(1, 2).Value = " 班组 ";
            worksheet.Cell(1, 3).Value = " 上班时间 ";
            worksheet.Cell(1, 4).Value = " 下班时间 ";

            worksheet.Cell(2, 1).Value = new DateTime(2026, 10, 20);
            worksheet.Cell(2, 2).Value = "甲班";
            worksheet.Cell(2, 3).Value = new TimeSpan(0, 0, 0);
            worksheet.Cell(2, 4).Value = new TimeSpan(8, 0, 0);
        });

        _parser.Parse("带空格表头.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(issues);
        Assert.Single(rows);
    }

    [Fact]
    public void Parse_Should_Mark_Rest_By_Time_Cell_Not_Only_By_Team_Name()
    {
        // 有些台账班组名正常，靠时间列写「休」表示轮休
        using MemoryStream stream = BuildWorkbook(worksheet =>
        {
            WriteHeader(worksheet, 1);
            worksheet.Cell(2, 1).Value = new DateTime(2026, 10, 20);
            worksheet.Cell(2, 2).Value = "甲班";
            worksheet.Cell(2, 3).Value = "休";
            worksheet.Cell(2, 4).Value = "休";
        });

        _parser.Parse("时间列轮休.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Empty(issues);
        Assert.True(Assert.Single(rows).IsRestDay);
    }

    [Fact]
    public void Parse_With_Corrupted_Stream_Should_Throw_Format_Exception()
    {
        // 随机字节不是有效的 xlsx，XLWorkbook 构造时会失败
        using var stream = new MemoryStream([0x00, 0x01, 0x02, 0x03]);

        Assert.ThrowsAny<Exception>(() => _parser.Parse("坏文件.xlsx", stream, out _, out _));
    }

    [Fact]
    public void Parse_Should_Return_Count_Of_Parsed_Rows()
    {
        using MemoryStream stream = BuildWorkbook(worksheet =>
        {
            WriteHeader(worksheet, 1);
            for (var i = 0; i < 5; i++)
            {
                worksheet.Cell(2 + i, 1).Value = new DateTime(2026, 10, 20);
                worksheet.Cell(2 + i, 2).Value = $"班{i}";
                worksheet.Cell(2 + i, 3).Value = new TimeSpan(i, 0, 0);
                worksheet.Cell(2 + i, 4).Value = new TimeSpan(i + 1, 0, 0);
            }
        });

        var parsed = _parser.Parse("五行.xlsx", stream, out List<ShiftScheduleRowDto> rows,
            out List<ShiftScheduleIssueDto> issues);

        Assert.Equal(5, parsed);
        Assert.Equal(5, rows.Count);
        Assert.Empty(issues);
    }

    #region 辅助

    private static void WriteHeader(IXLWorksheet worksheet, int row)
    {
        worksheet.Cell(row, 1).Value = "自然日";
        worksheet.Cell(row, 2).Value = "班组名称";
        worksheet.Cell(row, 3).Value = "上班时间";
        worksheet.Cell(row, 4).Value = "下班时间";
    }

    private static MemoryStream BuildWorkbook(Action<IXLWorksheet> fill)
    {
        var stream = new MemoryStream();

        using (var workbook = new XLWorkbook())
        {
            IXLWorksheet worksheet = workbook.Worksheets.Add("排班表");
            fill(worksheet);
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// 仅暴露长度、不含实际内容的流，用于测试体积上限分支。
    /// </summary>
    private sealed class LengthOnlyStream(long length) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return Position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void Flush()
        {
        }
    }

    #endregion
}
