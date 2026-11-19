using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAP.Core.Shared.Contracts;
using DAP.Presentation.AvaloniaApp.Common;
using DAP.Presentation.AvaloniaApp.Navigation;
using DAP.Presentation.AvaloniaApp.Services;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Pages;

/// <summary>
/// 排班日历：按月查看每日在岗与轮休班组（只读）。
///
/// 网格固定 6 行 × 7 列、周一为每周第一天，与 Web 端自建日历保持一致：
/// Avalonia/Semi 没有日历控件，月份长度不同时用空单元格补齐，避免行数跳动。
/// </summary>
public sealed partial class ShiftCalendarPageViewModel : PageViewModelBase
{
    private const int TotalCells = 42;

    private readonly DesktopSession _session;
    private DateOnly _month;

    /// <summary>
    /// 初始化页面。
    /// </summary>
    /// <param name="session">桌面会话。</param>
    public ShiftCalendarPageViewModel(DesktopSession session)
    {
        _session = session;
        _month = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);

        PreviousMonthCommand = new AsyncRelayCommand(() => ShiftMonthAsync(-1));
        NextMonthCommand = new AsyncRelayCommand(() => ShiftMonthAsync(1));
        CurrentMonthCommand = new AsyncRelayCommand(GoToCurrentMonthAsync);
    }

    /// <inheritdoc />
    public override string PageKey => PageKeys.ShiftCalendar;

    /// <inheritdoc />
    public override string Title => "排班日历";

    /// <inheritdoc />
    public override string Description => "按周查看每日在岗、轮休班组与覆盖完整性，排班导入请到 Web 端操作。";

    /// <inheritdoc />
    public override string Accent => "info";

    /// <summary>
    /// 月份标题。
    /// </summary>
    [ObservableProperty] private string _monthText = string.Empty;

    /// <summary>
    /// 是否有排班数据。
    /// </summary>
    [ObservableProperty] private bool _hasData;

    /// <summary>
    /// 汇总文案。
    /// </summary>
    [ObservableProperty] private string _summaryText = string.Empty;

    /// <summary>
    /// 日历单元格（固定 42 个）。
    /// </summary>
    public ObservableCollection<CalendarCellModel> Cells { get; } = [];

    /// <summary>
    /// 上一月。
    /// </summary>
    public IAsyncRelayCommand PreviousMonthCommand { get; }

    /// <summary>
    /// 下一月。
    /// </summary>
    public IAsyncRelayCommand NextMonthCommand { get; }

    /// <summary>
    /// 回到本月。
    /// </summary>
    public IAsyncRelayCommand CurrentMonthCommand { get; }

    /// <inheritdoc />
    protected override async Task LoadCoreAsync()
    {
        MonthText = $"{_month:yyyy 年 M 月}";

        DateOnly gridStart = ResolveGridStart(_month);
        DateOnly gridEnd = gridStart.AddDays(TotalCells - 1);

        IReadOnlyList<DailyShiftScheduleDto> days =
            await _session.Api.GetShiftScheduleCalendarAsync(gridStart, gridEnd);

        Dictionary<DateOnly, DailyShiftScheduleDto> lookup =
            days.ToDictionary(item => item.WorkDate);

        Cells.Clear();

        for (int index = 0; index < TotalCells; index++)
        {
            DateOnly date = gridStart.AddDays(index);
            bool inMonth = date.Month == _month.Month && date.Year == _month.Year;

            if (lookup.TryGetValue(date, out DailyShiftScheduleDto? day))
            {
                IBrush background = !day.CoverageComplete
                    ? Palette.Brush("DapWarningTintBrush")
                    : day.OnDutyTeams.Count == 0
                        ? Palette.Brush("DapSurfaceSunkenBrush")
                        : Palette.Brush("DapInfoTintBrush");

                IBrush accent = !day.CoverageComplete
                    ? Palette.Brush("DapWarningBrush")
                    : day.OnDutyTeams.Count == 0
                        ? Palette.Brush("DapTextSecondaryBrush")
                        : Palette.Brush("DapInfoBrush");

                Cells.Add(new CalendarCellModel(
                    date.Day.ToString("00"),
                    date.ToString("MM-dd"),
                    inMonth,
                    day.OnDutyTeams.Count == 0 ? "无在岗" : string.Join("、", day.OnDutyTeams),
                    day.RestTeams.Count == 0 ? "--" : string.Join("、", day.RestTeams),
                    day.CoverageComplete,
                    background,
                    accent));
            }
            else
            {
                Cells.Add(new CalendarCellModel(
                    date.Day.ToString("00"),
                    date.ToString("MM-dd"),
                    inMonth,
                    "未排班",
                    "--",
                    false,
                    Palette.Brush("DapSurfaceMutedBrush"),
                    Palette.Brush("DapTextSubtleBrush")));
            }
        }

        int scheduled = days.Count;
        int complete = days.Count(day => day.CoverageComplete);

        HasData = scheduled > 0;
        SummaryText = scheduled == 0
            ? "本月没有排班记录"
            : $"本月已排班 {scheduled} 天，其中 {complete} 天满足四班三运转完整覆盖";

        if (scheduled == 0)
            SetMessage("当前区间内没有排班数据，请先在 Web 端「业务配置 → 排班时间管理」中导入排班表。");
        else
            SetMessage(null);
    }

    /// <summary>
    /// 计算日历网格起点：周一为一周第一天。
    /// </summary>
    private static DateOnly ResolveGridStart(DateOnly month)
    {
        DateOnly first = new(month.Year, month.Month, 1);
        int offset = ((int) first.DayOfWeek + 6) % 7;

        return first.AddDays(-offset);
    }

    /// <summary>
    /// 切换月份。
    /// </summary>
    private async Task ShiftMonthAsync(int delta)
    {
        _month = _month.AddMonths(delta);
        await ReloadAsync();
    }

    /// <summary>
    /// 回到本月。
    /// </summary>
    private async Task GoToCurrentMonthAsync()
    {
        _month = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        await ReloadAsync();
    }
}