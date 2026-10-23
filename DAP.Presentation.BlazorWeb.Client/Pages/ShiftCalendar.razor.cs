using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 表示排班日历页的交互逻辑。
/// </summary>
public partial class ShiftCalendar
{
    /// <summary>
    /// 「全部班组」筛选项使用的哨兵值，与真实班组编码不可能冲突。
    /// </summary>
    private const string AllTeamsValue = "__all__";

    /// <summary>
    /// 单元格内最多预览的班次数量，超出部分折叠为「还有 N 个班次」。
    /// </summary>
    private const int MaxShiftPreview = 3;

    private static readonly string[] WeekdayLabels = ["一", "二", "三", "四", "五", "六", "日"];

    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;

    private DateOnly _monthAnchor = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private IReadOnlyCollection<DailyShiftScheduleDto> _calendar = [];
    private IReadOnlyCollection<ShiftTeamDto> _teams = [];
    private IReadOnlyList<CalendarCell> _cells = [];
    private DailyShiftScheduleDto? _selectedDay;
    private TeamShiftScheduleDto? _teamSummary;
    private string? _selectedTeamCode;
    private string _selectedTeamName = string.Empty;
    private bool _isLoading = true;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;

    private string _monthLabel => _monthAnchor.ToString("yyyy 年 MM 月");

    private string _selectedDateText => _selectedDay is null
        ? "未选择日期"
        : $"{_selectedDay.WorkDate:yyyy-MM-dd}（{FormatWeekday(_selectedDay.WorkDate)}）";

    private double DailyAverageHours
    {
        get
        {
            if (_teamSummary is null || _teamSummary.TotalShifts == 0)
            {
                return 0d;
            }

            return Math.Round(_teamSummary.TotalHours / _teamSummary.TotalShifts, 2);
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _isLoading = true;
        _message = null;

        try
        {
            ShiftScheduleStatusDto status = await PlatformApiClient.GetShiftScheduleStatusAsync();
            _teams = status.Teams;

            DateOnly rangeStart = _monthAnchor.AddDays(-7);
            DateOnly rangeEnd = _monthAnchor.AddMonths(1).AddDays(7);

            _calendar = await PlatformApiClient.GetShiftScheduleCalendarAsync(
                rangeStart, rangeEnd, _selectedTeamCode);

            BuildCells();

            if (_selectedDay is not null)
            {
                _selectedDay = _calendar.FirstOrDefault(item => item.WorkDate == _selectedDay.WorkDate);
            }
            else
            {
                _selectedDay = _calendar.FirstOrDefault(item => item.WorkDate == DateOnly.FromDateTime(DateTime.Today))
                               ?? _calendar.FirstOrDefault();
            }

            await LoadTeamSummaryAsync();
        }
        catch (Exception ex)
        {
            _message = $"排班日历加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// 月历需要补齐首尾空白格，才能让每周从周一开始对齐。
    /// </summary>
    private void BuildCells()
    {
        Dictionary<DateOnly, DailyShiftScheduleDto> index = _calendar
            .ToDictionary(item => item.WorkDate);

        DateOnly monthStart = _monthAnchor;
        DateOnly monthEnd = _monthAnchor.AddMonths(1).AddDays(-1);

        // .NET 的 DayOfWeek 以周日为 0，这里转换成以周一为 0 的偏移。
        var leading = ((int) monthStart.DayOfWeek + 6) % 7;
        DateOnly gridStart = monthStart.AddDays(-leading);

        List<CalendarCell> cells = [];

        for (var offset = 0; offset < 42; offset++)
        {
            DateOnly date = gridStart.AddDays(offset);

            // 整周都落在本月之外时不再生成，避免尾部出现整行空白格。
            if (offset >= 35 && date > monthEnd)
            {
                break;
            }

            index.TryGetValue(date, out DailyShiftScheduleDto? day);

            cells.Add(new CalendarCell(
                date,
                date < monthStart || date > monthEnd,
                date == DateOnly.FromDateTime(DateTime.Today),
                day is not null,
                day?.Rows.Where(item => !item.IsRestDay).OrderBy(item => item.StartTime).ToArray() ?? [],
                day?.Rows.Any(item => item.IsRestDay) ?? false,
                day?.CoverageComplete ?? false));
        }

        _cells = cells;
    }

    private async Task LoadTeamSummaryAsync()
    {
        if (_selectedTeamCode is null)
        {
            _teamSummary = null;
            _selectedTeamName = string.Empty;
            return;
        }

        _selectedTeamName = _teams.FirstOrDefault(team => team.TeamCode == _selectedTeamCode)?.TeamName
                            ?? _selectedTeamCode;

        try
        {
            _teamSummary = await PlatformApiClient.GetTeamShiftScheduleAsync(
                _selectedTeamCode, _monthAnchor, _monthAnchor.AddMonths(1).AddDays(-1));
        }
        catch (Exception)
        {
            // 该班组在所选月份可能完全没有排班，此时保持空态而不是中断整个页面。
            _teamSummary = null;
        }
    }

    private async Task OnTeamChangedAsync(string value)
    {
        _selectedTeamCode = value == AllTeamsValue ? null : value;
        await LoadAsync();
    }

    private void SelectDate(DateOnly date)
    {
        _selectedDay = _calendar.FirstOrDefault(item => item.WorkDate == date);
    }

    private async Task ShiftMonth(int delta)
    {
        _monthAnchor = _monthAnchor.AddMonths(delta);
        _selectedDay = null;
        await LoadAsync();
    }

    private async Task GoToCurrentMonth()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        _monthAnchor = new DateOnly(today.Year, today.Month, 1);
        _selectedDay = null;
        await LoadAsync();
    }

    private string BuildCellClass(CalendarCell cell)
    {
        List<string> classes = ["platform-calendar-cell"];

        if (cell.IsOutsideMonth)
        {
            classes.Add("platform-calendar-cell--muted");
        }

        if (cell.IsToday)
        {
            classes.Add("platform-calendar-cell--today");
        }

        if (_selectedDay?.WorkDate == cell.Date)
        {
            classes.Add("platform-calendar-cell--selected");
        }

        return string.Join(' ', classes);
    }

    private static string FormatWeekday(DateOnly date)
    {
        return date.DayOfWeek switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            _ => "周日"
        };
    }

    /// <summary>
    /// 月历中的一个格子。
    /// </summary>
    private sealed record CalendarCell(
        DateOnly Date,
        bool IsOutsideMonth,
        bool IsToday,
        bool HasSchedule,
        IReadOnlyList<ShiftScheduleRowDto> DutyShifts,
        bool HasRest,
        bool IsComplete);
}
