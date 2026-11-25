using ClosedXML.Excel;
using DAP.Core.Shared.Contracts;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 班组排班表 Excel 解析器。
///
/// 支持「宽表」与「长表」两种真实台账格式，按表头自动识别：
/// 宽表（现场业务标准格式）——A 列自然日，B/C/D 三列表头为「夜班（0-8）」「白班（8-16）」
/// 「中班（16-24）」，单元格填班组编号 1–4（1=班组-甲、2=班组-乙、3=班组-丙、4=班组-丁），
/// 每行展开为 3 条班次记录；
/// 长表（兼容旧版）——「自然日 / 班组名称 / 上班时间 / 下班时间」四列，列顺序可任意调整。
/// </summary>
public sealed class ShiftScheduleExcelParser
{
    /// <summary>
    /// 上传体积上限，防止超大文件拖垮服务端。
    /// </summary>
    public const int MaxFileSizeBytes = 8 * 1024 * 1024;

    private const int MaxScanRows = 20000;

    private static readonly string[] WorkDateKeywords = ["自然日", "日期", "date", "班次日期"];
    private static readonly string[] TeamKeywords = ["班组", "班次名称", "班组名称", "team", "班组名"];
    private static readonly string[] StartKeywords = ["上班", "开始", "start", "出勤"];
    private static readonly string[] EndKeywords = ["下班", "结束", "end", "收工"];

    /// <summary>宽表表头的班次列关键字：命中全部三个即判定为宽表格式。</summary>
    private static readonly string[] NightShiftKeywords = ["夜班"];
    private static readonly string[] DayShiftKeywords = ["白班"];
    private static readonly string[] MiddleShiftKeywords = ["中班"];

    /// <summary>
    /// 宽表班次列的固定时间：表头标注「夜班（0-8）/白班（8-16）/中班（16-24）」
    /// 指每天 24 小时制，三班首尾相接。中班结束时刻 24:00 统一存为 00:00（跨天约定）。
    /// </summary>
    private static readonly (string ShiftName, TimeOnly Start, TimeOnly End, double Hours)[] FixedShifts =
    [
        ("夜班", new TimeOnly(0, 0), new TimeOnly(8, 0), 8d),
        ("白班", new TimeOnly(8, 0), new TimeOnly(16, 0), 8d),
        ("中班", new TimeOnly(16, 0), new TimeOnly(0, 0), 8d)
    ];

    /// <summary>
    /// 宽表班组编号到名称的默认映射：1=班组-甲、2=班组-乙、3=班组-丙、4=班组-丁。
    /// 编号超出 1–4 视为脏数据报错，避免静默生成现场不认识的班组。
    /// </summary>
    internal static readonly string[] DefaultTeamNames = ["班组-甲", "班组-乙", "班组-丙", "班组-丁"];

    /// <summary>
    /// 解析上传的 Excel 排班表。
    /// </summary>
    /// <param name="fileName">文件名，用于错误提示。</param>
    /// <param name="stream">文件内容流。</param>
    /// <param name="rows">解析出的班次记录。</param>
    /// <param name="parseIssues">解析阶段的问题，仅包含无法读取的单元格。</param>
    /// <returns>解析出的记录数量。</returns>
    public int Parse(
        string fileName,
        Stream stream,
        out List<ShiftScheduleRowDto> rows,
        out List<ShiftScheduleIssueDto> parseIssues)
    {
        rows = [];
        parseIssues = [];

        if (stream.CanSeek && stream.Length > MaxFileSizeBytes)
        {
            parseIssues.Add(new ShiftScheduleIssueDto(
                0, $"文件体积 {stream.Length / 1024} KB 超过 {MaxFileSizeBytes / 1024} KB 上限。", "error"));
            return 0;
        }

        using XLWorkbook workbook = new(stream);
        IXLWorksheet worksheet = workbook.Worksheets.FirstOrDefault()
                                 ?? throw new ShiftScheduleFormatException("Excel 中没有可用的工作表。");

        if (worksheet.RangeUsed() is not { } usedRange)
        {
            parseIssues.Add(new ShiftScheduleIssueDto(0, "工作表为空，没有读取到任何内容。", "error"));
            return 0;
        }

        int lastRow = Math.Min(usedRange.LastRow().RowNumber(), MaxScanRows);

        if (usedRange.LastRow().RowNumber() > MaxScanRows)
        {
            parseIssues.Add(new ShiftScheduleIssueDto(
                0, $"数据行超过 {MaxScanRows} 行，仅解析前 {MaxScanRows} 行。", "warning"));
        }

        // 宽表（夜班/白班/中班三列班组编号）是现场业务的标准格式，优先识别。
        if (TryFindWideHeaderRow(worksheet, usedRange, lastRow,
                out int wideHeaderRow, out WideHeaderColumns wideColumns))
        {
            return ParseWideRows(worksheet, wideHeaderRow, lastRow, wideColumns, rows, parseIssues);
        }

        int headerRow = FindHeaderRow(usedRange, lastRow, out HeaderColumns columns, out string headerProblem);

        if (!string.IsNullOrEmpty(headerProblem))
        {
            parseIssues.Add(new ShiftScheduleIssueDto(headerRow, headerProblem, "error"));
            return 0;
        }

        Dictionary<string, string> teams = new(StringComparer.OrdinalIgnoreCase);

        for (int rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            ParseRow(worksheet, rowNumber, columns, teams, rows, parseIssues);
        }

        return rows.Count;
    }

    /// <summary>
    /// 宽表表头列定位结果：三个班次列 + 从数据区推断出的自然日列。
    /// </summary>
    private sealed class WideHeaderColumns(
        int dateColumn,
        int nightColumn,
        int dayColumn,
        int middleColumn)
    {
        public int DateColumn { get; } = dateColumn;
        public int NightColumn { get; } = nightColumn;
        public int DayColumn { get; } = dayColumn;
        public int MiddleColumn { get; } = middleColumn;
    }

    /// <summary>
    /// 探测宽表表头：某行同时含「夜班」「白班」「中班」三个班次标注即命中。
    ///
    /// 宽表的日期列在表头里往往是空单元格（如真实台账的 A1 为空），
    /// 因此日期列不在表头阶段定位，而是在解析前按「数据区最先出现日期值的列」推断。
    /// </summary>
    private static bool TryFindWideHeaderRow(
        IXLWorksheet worksheet,
        IXLRange usedRange,
        int lastRow,
        out int headerRow,
        out WideHeaderColumns columns)
    {
        headerRow = 0;
        columns = new WideHeaderColumns(0, 0, 0, 0);

        int firstColumn = usedRange.FirstColumn().ColumnNumber();
        int lastColumn = usedRange.LastColumn().ColumnNumber();
        int firstRow = usedRange.FirstRow().RowNumber();

        for (int row = firstRow; row <= Math.Min(lastRow, firstRow + 4); row++)
        {
            int nightColumn = 0;
            int dayColumn = 0;
            int middleColumn = 0;

            for (int column = firstColumn; column <= lastColumn; column++)
            {
                string text = NormalizeHeaderText(worksheet.Cell(row, column).GetString());

                nightColumn = MatchColumn(nightColumn, column, text, NightShiftKeywords);
                dayColumn = MatchColumn(dayColumn, column, text, DayShiftKeywords);
                middleColumn = MatchColumn(middleColumn, column, text, MiddleShiftKeywords);
            }

            if (nightColumn > 0 && dayColumn > 0 && middleColumn > 0)
            {
                headerRow = row;
                columns = new WideHeaderColumns(
                    ResolveWideDateColumn(worksheet, row, lastRow, nightColumn, dayColumn, middleColumn),
                    nightColumn, dayColumn, middleColumn);

                // 日期列推断失败（数据区没有可识别日期）时不判为宽表，
                // 让调用方落到长表探测给出统一的表头错误。
                return columns.DateColumn > 0;
            }
        }

        return false;
    }

    /// <summary>
    /// 推断宽表的自然日列：取三个班次列之外、数据区前几行中第一个可识别出日期的列。
    /// 真实台账的日期列表头是空单元格，不能靠表头文本定位。
    /// </summary>
    private static int ResolveWideDateColumn(
        IXLWorksheet worksheet,
        int headerRow,
        int lastRow,
        params int[] shiftColumns)
    {
        int firstColumn = worksheet.RangeUsed()!.FirstColumn().ColumnNumber();
        int lastColumn = worksheet.RangeUsed()!.LastColumn().ColumnNumber();
        int sampleRows = Math.Min(lastRow, headerRow + 5);

        for (int column = firstColumn; column <= lastColumn; column++)
        {
            if (shiftColumns.Contains(column))
            {
                continue;
            }

            for (int row = headerRow + 1; row <= sampleRows; row++)
            {
                if (!worksheet.Cell(row, column).IsEmpty() && TryReadDate(worksheet.Cell(row, column), out _))
                {
                    return column;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// 解析宽表数据区：每行按「日期 + 三个班次列的班组编号」展开为 3 条班次记录。
    /// </summary>
    private static int ParseWideRows(
        IXLWorksheet worksheet,
        int headerRow,
        int lastRow,
        WideHeaderColumns columns,
        List<ShiftScheduleRowDto> rows,
        List<ShiftScheduleIssueDto> issues)
    {
        // 班次列与 FixedShifts 索引的对应关系：B=夜班(0)、C=白班(1)、D=中班(2)。
        (int ColumnNumber, int ShiftIndex)[] shiftColumnPairs =
        [
            (columns.NightColumn, 0),
            (columns.DayColumn, 1),
            (columns.MiddleColumn, 2)
        ];

        for (int rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            IXLCell dateCell = worksheet.Cell(rowNumber, columns.DateColumn);

            if (dateCell.IsEmpty() &&
                shiftColumnPairs.All(pair => worksheet.Cell(rowNumber, pair.Item1).IsEmpty()))
            {
                // 整行为空：Excel 常见的尾部空行，直接跳过。
                continue;
            }

            if (!TryReadDate(dateCell, out DateOnly workDate))
            {
                issues.Add(new ShiftScheduleIssueDto(rowNumber,
                    $"自然日无法识别：\"{dateCell.GetFormattedString()}\"，请使用日期格式（如 2026年10月11日）。", "error"));
                continue;
            }

            foreach ((int columnNumber, int shiftIndex) in shiftColumnPairs)
            {
                IXLCell cell = worksheet.Cell(rowNumber, columnNumber);
                (string shiftName, TimeOnly start, TimeOnly end, double hours) = FixedShifts[shiftIndex];

                if (cell.IsEmpty())
                {
                    // 某个班次没人值班：如实按空处理并给出提示，不阻塞其他列。
                    issues.Add(new ShiftScheduleIssueDto(rowNumber,
                        $"{shiftName}（{start:HH\\:mm}-{(hours > 0 && end == TimeOnly.MinValue ? "24:00" : $"{end:HH\\:mm}")}）班组编号为空。",
                        "warning"));
                    continue;
                }

                if (!TryReadTeamNumber(cell, out int teamNumber))
                {
                    issues.Add(new ShiftScheduleIssueDto(rowNumber,
                        $"{shiftName}列的班组编号无法识别：\"{cell.GetFormattedString()}\"，请填写 1–4 的数字" +
                        $"（1={DefaultTeamNames[0]}、2={DefaultTeamNames[1]}、3={DefaultTeamNames[2]}、4={DefaultTeamNames[3]}）。",
                        "error"));
                    continue;
                }

                if (teamNumber < 1 || teamNumber > DefaultTeamNames.Length)
                {
                    issues.Add(new ShiftScheduleIssueDto(rowNumber,
                        $"{shiftName}列的班组编号 {teamNumber} 超出范围，只支持 1–4" +
                        $"（1={DefaultTeamNames[0]}、2={DefaultTeamNames[1]}、3={DefaultTeamNames[2]}、4={DefaultTeamNames[3]}）。",
                        "error"));
                    continue;
                }

                string teamName = DefaultTeamNames[teamNumber - 1];
                rows.Add(new ShiftScheduleRowDto(
                    workDate,
                    NormalizeTeamCode(teamName),
                    teamName,
                    shiftName,
                    start,
                    end,
                    false,
                    hours));
            }
        }

        return rows.Count;
    }

    /// <summary>
    /// 读取宽表的班组编号：Excel 数字单元格（1、1.0）或纯数字文本（"1"）都接受。
    /// </summary>
    private static bool TryReadTeamNumber(IXLCell cell, out int teamNumber)
    {
        teamNumber = 0;

        if (cell.TryGetValue<double>(out double value) && value == Math.Floor(value))
        {
            teamNumber = (int)value;
            return true;
        }

        string text = cell.GetFormattedString().Trim();

        return int.TryParse(text, out teamNumber);
    }

    private static int FindHeaderRow(
        IXLRange usedRange,
        int lastRow,
        out HeaderColumns columns,
        out string headerProblem)
    {
        columns = new HeaderColumns(0, 0, 0, 0);
        headerProblem = string.Empty;

        int firstColumn = usedRange.FirstColumn().ColumnNumber();
        int lastColumn = usedRange.LastColumn().ColumnNumber();
        int firstRow = usedRange.FirstRow().RowNumber();

        // 表头前可能置换成两行说明文字，最多向下探测 5 行。
        for (int row = firstRow; row <= Math.Min(lastRow, firstRow + 4); row++)
        {
            HeaderColumns candidate = new(0, 0, 0, 0);

            for (int column = firstColumn; column <= lastColumn; column++)
            {
                string text = NormalizeHeaderText(usedRange.Cell(row, column).GetString());
                if (text.Length == 0)
                {
                    continue;
                }

                candidate.DateColumn = MatchColumn(candidate.DateColumn, column, text, WorkDateKeywords);
                candidate.TeamColumn = MatchColumn(candidate.TeamColumn, column, text, TeamKeywords);
                candidate.StartColumn = MatchColumn(candidate.StartColumn, column, text, StartKeywords);
                candidate.EndColumn = MatchColumn(candidate.EndColumn, column, text, EndKeywords);
            }

            if (candidate.IsComplete)
            {
                columns = candidate;
                return row;
            }
        }

        headerProblem =
            "未能识别表头，请确保文件中包含「自然日」「班组名称」「上班时间」「下班时间」四列。";
        return firstRow;
    }

    /// <summary>
    /// 该单元格文字命中关键字时返回其真实列号，否则保留已有匹配结果。
    /// </summary>
    /// <summary>
    /// 归一化表头文本：去掉全部空白字符（含全角空格）。
    ///
    /// 现场台账的表头常从 PDF、网页或邮件里粘贴过来，中间会带空格
    /// （如「自 然 日」），直接用 Contains 匹配关键字会失败。
    /// </summary>
    private static string NormalizeHeaderText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        Span<char> buffer = raw.Length <= 128 ? stackalloc char[raw.Length] : new char[raw.Length];
        int length = 0;

        foreach (char ch in raw)
        {
            if (!char.IsWhiteSpace(ch) && ch != '　')
            {
                buffer[length++] = ch;
            }
        }

        return new string(buffer[..length]);
    }

    private static int MatchColumn(int current, int column, string text, string[] keywords)
    {
        if (current > 0)
        {
            return current;
        }

        return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ? column : 0;
    }

    /// <summary>
    /// 表头列定位结果。表头探测阶段需要逐列累加匹配结果，因此属性必须是可变的。
    /// </summary>
    private sealed class HeaderColumns(int dateColumn, int teamColumn, int startColumn, int endColumn)
    {
        public int DateColumn { get; set; } = dateColumn;

        public int TeamColumn { get; set; } = teamColumn;

        public int StartColumn { get; set; } = startColumn;

        public int EndColumn { get; set; } = endColumn;

        public bool IsComplete => DateColumn > 0 && TeamColumn > 0 && StartColumn > 0 && EndColumn > 0;
    }

    private static void ParseRow(
        IXLWorksheet worksheet,
        int rowNumber,
        HeaderColumns columns,
        Dictionary<string, string> teams,
        List<ShiftScheduleRowDto> rows,
        List<ShiftScheduleIssueDto> issues)
    {
        IXLCell dateCell = worksheet.Cell(rowNumber, columns.DateColumn);
        IXLCell teamCell = worksheet.Cell(rowNumber, columns.TeamColumn);
        IXLCell startCell = worksheet.Cell(rowNumber, columns.StartColumn);
        IXLCell endCell = worksheet.Cell(rowNumber, columns.EndColumn);

        // 整行为空时视为 Excel 中的空行，直接跳过而不是报错。
        if (dateCell.IsEmpty() && teamCell.IsEmpty() && startCell.IsEmpty() && endCell.IsEmpty())
        {
            return;
        }

        if (!TryReadDate(dateCell, out DateOnly workDate))
        {
            issues.Add(new ShiftScheduleIssueDto(rowNumber,
                $"自然日无法识别：\"{dateCell.GetFormattedString()}\"，请使用日期格式或 2026-10-20 这样的文本。", "error"));
            return;
        }

        string teamName = teamCell.GetFormattedString().Trim();
        if (teamName.Length == 0)
        {
            issues.Add(new ShiftScheduleIssueDto(rowNumber, "班组名称为空。", "error"));
            return;
        }

        string teamCode = NormalizeTeamCode(teamName);
        teams.TryAdd(teamCode, teamName);

        if (IsRestRow(teamName, startCell, endCell))
        {
            rows.Add(new ShiftScheduleRowDto(
                workDate, teamCode, teamName, "休息", TimeOnly.MinValue, TimeOnly.MinValue, true, 0d));
            return;
        }

        if (!TryReadTime(startCell, out TimeOnly startTime))
        {
            issues.Add(new ShiftScheduleIssueDto(rowNumber,
                $"上班时间无法识别：\"{startCell.GetFormattedString()}\"，请使用 08:00 这样的时间格式。", "error"));
            return;
        }

        if (!TryReadTime(endCell, out TimeOnly endTime))
        {
            issues.Add(new ShiftScheduleIssueDto(rowNumber,
                $"下班时间无法识别：\"{endCell.GetFormattedString()}\"，请使用 16:00 这样的时间格式；" +
                "跨天班次（如 16:00-次日）请填 00:00，不要填 24:00。", "error"));
            return;
        }

        // 允许把跨天的班次写成 16:00 - 次日 00:00，这里统一折算为 24 小时内的时刻。
        if (endTime < startTime)
        {
            endTime = new TimeOnly(0, 0);
        }

        // 跨天班次的工时 = 当日剩余小时 + 次日小时：16:00-00:00 是 8 小时而非 24 小时。
        double durationHours = endTime < startTime
            ? Math.Round(
                (TimeSpan.FromHours(24) - startTime.ToTimeSpan() + endTime.ToTimeSpan()).TotalHours, 2)
            : Math.Round((endTime - startTime).TotalHours, 2);
        if (durationHours <= 0)
        {
            durationHours = 24d;
            endTime = new TimeOnly(0, 0);
        }

        rows.Add(new ShiftScheduleRowDto(
            workDate,
            teamCode,
            teamName,
            ResolveShiftName(startTime),
            startTime,
            endTime,
            false,
            durationHours));
    }

    private static bool IsRestRow(string teamName, IXLCell startCell, IXLCell endCell)
    {
        return teamName.Contains("休", StringComparison.Ordinal) ||
               startCell.GetString().Contains("休", StringComparison.Ordinal) ||
               endCell.GetString().Contains("休", StringComparison.Ordinal);
    }

    /// <summary>
    /// 班组编码由名称派生：去掉结尾的「班」字后加 TEAM- 前缀。
    /// 统一派生规则很关键——排班、能耗、日历三处都用这个编码指代同一个班组，
    /// 若各处自行截断，能耗页面就会指向一个不存在的班组编码。
    /// </summary>
    public static string NormalizeTeamCode(string teamName)
    {
        string trimmed = teamName.Trim();

        // 「甲班」与「甲」都应得到 TEAM-甲，便于现场两种写法都能对上。
        string shortName = trimmed.EndsWith('班') ? trimmed[..^1] : trimmed;

        return $"TEAM-{shortName.ToUpperInvariant()}";
    }

    private static string ResolveShiftName(TimeOnly startTime)
    {
        int hour = startTime.Hour;

        return hour switch
        {
            < 6 => "夜班",
            < 14 => "早班",
            < 22 => "中班",
            _ => "晚班"
        };
    }

    private static bool TryReadDate(IXLCell cell, out DateOnly value)
    {
        value = default;

        if (cell.DataType is XLDataType.DateTime)
        {
            value = DateOnly.FromDateTime(cell.GetDateTime());
            return true;
        }

        // TimeSpan 型单元格若被误放到日期列，其值可能远超一天，此时应按文本再试一次。
        if (cell.TryGetValue<TimeSpan>(out TimeSpan raw) && IsPlausibleTimeSpan(raw))
        {
            value = DateOnly.FromDateTime(cell.GetDateTime());
            return true;
        }

        if (cell.TryGetValue<DateTime>(out DateTime dateTime))
        {
            value = DateOnly.FromDateTime(dateTime);
            return true;
        }

        string text = cell.GetFormattedString().Trim();
        if (text.Length == 0)
        {
            return false;
        }

        string[] formats =
        [
            "yyyy-MM-dd", "yyyy/M/d", "yyyy/MM/dd", "yyyy.MM.dd", "yyyyMMdd", "M/d", "yyyy年M月d日"
        ];

        return DateOnly.TryParseExact(text, formats, System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out value) ||
               DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out value);
    }

    private static bool TryReadTime(IXLCell cell, out TimeOnly value)
    {
        value = default;

        if (cell.DataType is XLDataType.DateTime)
        {
            value = TimeOnly.FromDateTime(cell.GetDateTime());
            return true;
        }

        // 一些写库工具（含 openpyxl）会把纯时刻写成 TimeSpan 型单元格，
        // 而 ClosedXML 的 TimeSpan 转换可能带回 Excel 日期序列号，
        // 因此这里只取一天内的时刻部分，超出 24 小时说明它是完整日期而非时刻。
        if (cell.TryGetValue<TimeSpan>(out TimeSpan timeSpan))
        {
            if (IsPlausibleTimeSpan(timeSpan))
            {
                value = TimeOnly.FromTimeSpan(timeSpan);
                return true;
            }

            if (cell.TryGetValue<DateTime>(out DateTime dateTime))
            {
                value = TimeOnly.FromDateTime(dateTime);
                return true;
            }

            return false;
        }

        // Excel 内部用小数表示一天中的时间比例，这里换算成小时分钟。
        if (cell.TryGetValue<double>(out double fraction) && fraction is > 0 and <= 1)
        {
            // 序列号 1 表示 24:00，即跨天班次的结束时刻，等价于次日 00:00
            if (fraction >= 1d)
            {
                value = TimeOnly.MinValue;
                return true;
            }

            int totalMinutes = (int)Math.Round(fraction * 24 * 60, MidpointRounding.AwayFromZero);
            value = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(totalMinutes % (24 * 60)));
            return true;
        }

        string text = cell.GetFormattedString().Trim();
        if (text.Length == 0)
        {
            return false;
        }

        // 「24:00」是现场最常见的误填，Excel 会把它存成序列号 1.0（走上面的分支）；
        // 而当它以文本形式保留时，这里显式归一化为 00:00，避免直接报错打断整个导入。
        if (string.Equals(text, "24:00", StringComparison.Ordinal) ||
            string.Equals(text, "24：00", StringComparison.Ordinal) ||
            string.Equals(text, "24:00:00", StringComparison.Ordinal))
        {
            value = TimeOnly.MinValue;
            return true;
        }

        string[] formats = ["H\\:mm", "HH\\:mm", "H\\:mm\\:ss", "HH\\:mm\\:ss", "H时mm分", "HH时mm分", "H点mm"];

        return TimeOnly.TryParseExact(text, formats, System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out value) ||
               TimeOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out value);
    }

    /// <summary>
    /// 判断 TimeSpan 是否可解释为一天内的时刻。
    ///
    /// 上界必须是**严格小于** 24 小时而非 ≤：Text写了 `24:00` 的单元格
    /// 会被解析成正好 24 小时的 TimeSpan，而 <see cref="TimeOnly"/> 的取值范围到 24 小时为止，
    /// `TimeOnly.FromTimeSpan` 会在恰好 24:00 时抛 ArgumentOutOfRangeException。
    /// 把 24:00 排除在外，它会落到下方的文本解析分支，由调用方给出「请填 00:00」的可读提示。
    /// </summary>
    private static bool IsPlausibleTimeSpan(TimeSpan value)
    {
        return value >= TimeSpan.Zero && value < TimeSpan.FromHours(24);
    }
}

/// <summary>
/// 表示排班 Excel 的结构性问题，例如工作表缺失或文件无法解密。
/// </summary>
public sealed class ShiftScheduleFormatException : Exception
{
    /// <summary>
    /// 初始化一个新的 <see cref="ShiftScheduleFormatException"/> 实例。
    /// </summary>
    /// <param name="message">错误描述。</param>
    public ShiftScheduleFormatException(string message) : base(message)
    {
    }
}
