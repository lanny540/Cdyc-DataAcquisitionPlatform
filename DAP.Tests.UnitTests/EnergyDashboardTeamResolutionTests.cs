using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 仪表板班组归属解析的守卫测试：
/// 默认班组必须走严格解析——不命中就不给在岗事实，绝不取模猜测。
/// </summary>
public sealed class EnergyDashboardTeamResolutionTests
{
    /// <summary>
    /// 固定时钟：2026-11-03 12:00（本地时间 +8），处于白班区间内。
    /// </summary>
    private static TimeProvider FixedClock(int hour = 12)
    {
        DateTimeOffset local = new(2026, 11, 3, hour, 0, 0, TimeSpan.FromHours(8));
        return new FixedTimeProvider(local);
    }

    private static IShiftSchedulingService ServiceWithRows(params ShiftScheduleRowDto[] rows)
    {
        return new StubShiftSchedulingService(rows);
    }

    private static ShiftScheduleRowDto Row(
        string teamCode,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        bool restDay = false)
    {
        return new ShiftScheduleRowDto(date, teamCode, $"{teamCode}班", "白班", start, end,
            restDay, (end - start).TotalHours);
    }

    [Fact]
    public async Task OnDuty_Team_Should_Resolve_When_Schedule_Covers_Now()
    {
        IShiftSchedulingService service = ServiceWithRows(
            Row("TEAM-甲", new DateOnly(2026, 11, 3), new TimeOnly(8, 0), new TimeOnly(16, 0)));
        DeterministicEnergyMetricService energy = new(service, FixedClock(12));

        EnergyDashboardDto dashboard = await energy.GetDashboardAsync();

        Assert.Equal("TEAM-甲", dashboard.TeamCode);
        Assert.Equal("on-duty", dashboard.TeamResolution);
    }

    [Fact]
    public async Task No_Schedule_Should_Not_Fabricate_OnDuty_Team()
    {
        IShiftSchedulingService service = ServiceWithRows();
        DeterministicEnergyMetricService energy = new(service, FixedClock(12));

        EnergyDashboardDto dashboard = await energy.GetDashboardAsync();

        // 排班未导入：仍返回数据（不隐藏），但必须显式标注 no-schedule，
        // 页面据此提示「排班数据尚未导入」，而不是把第一个班组当成在岗事实。
        Assert.Equal("no-schedule", dashboard.TeamResolution);
    }

    [Fact]
    public async Task RestDay_Should_Not_Guess_Team_By_Modulo()
    {
        // 当天四个班组全部轮休：旧实现会按 today.DayNumber % 4 猜一个班组，
        // 车间终端会把猜出来的班组当成在岗事实——这是必须堵住的行为。
        DateOnly today = new(2026, 11, 3);

        IShiftSchedulingService service = ServiceWithRows(
            Row("TEAM-甲", today, new TimeOnly(8, 0), new TimeOnly(16, 0), true),
            Row("TEAM-乙", today, new TimeOnly(8, 0), new TimeOnly(16, 0), true),
            Row("TEAM-丙", today, new TimeOnly(8, 0), new TimeOnly(16, 0), true),
            Row("TEAM-丁", today, new TimeOnly(8, 0), new TimeOnly(16, 0), true));
        DeterministicEnergyMetricService energy = new(service, FixedClock(12));

        EnergyDashboardDto dashboard = await energy.GetDashboardAsync();

        Assert.Equal("rest-day", dashboard.TeamResolution);
    }

    [Fact]
    public async Task BetweenShifts_Should_Not_Resolve_OnDuty()
    {
        // 12:00 处在 08:00-12:00 与 16:00-24:00 之间的空档：当前时刻不在任何班次内。
        DateOnly today = new(2026, 11, 3);

        IShiftSchedulingService service = ServiceWithRows(
            Row("TEAM-甲", today, new TimeOnly(8, 0), new TimeOnly(12, 0)),
            Row("TEAM-乙", today, new TimeOnly(16, 0), new TimeOnly(23, 59)));
        DeterministicEnergyMetricService energy = new(service, FixedClock(12));

        EnergyDashboardDto dashboard = await energy.GetDashboardAsync();

        Assert.Equal("rest-day", dashboard.TeamResolution);
    }

    [Fact]
    public async Task Manual_Team_Should_Be_Marked_As_Manual()
    {
        IShiftSchedulingService service = ServiceWithRows(
            Row("TEAM-甲", new DateOnly(2026, 11, 3), new TimeOnly(8, 0), new TimeOnly(16, 0)));
        DeterministicEnergyMetricService energy = new(service, FixedClock(12));

        EnergyDashboardDto dashboard = await energy.GetDashboardAsync(teamCode: "TEAM-乙");

        Assert.Equal("TEAM-乙", dashboard.TeamCode);
        Assert.Equal("manual", dashboard.TeamResolution);
    }

    [Fact]
    public async Task Manual_Selection_Of_OnDuty_Team_Should_Stay_OnDuty()
    {
        // 回归：用户先自动落在在岗班组（徽标「当前班组」），切走再手动切回后，
        // 徽标错误显示「非在岗」——根因是手动指定一律返回 manual，
        // 服务端不检查所选班组是否恰好在岗。手动切回在岗班组必须保持 on-duty。
        IShiftSchedulingService service = ServiceWithRows(
            Row("TEAM-甲", new DateOnly(2026, 11, 3), new TimeOnly(8, 0), new TimeOnly(16, 0)));
        DeterministicEnergyMetricService energy = new(service, FixedClock(12));

        EnergyDashboardDto dashboard = await energy.GetDashboardAsync(teamCode: "TEAM-甲");

        Assert.Equal("TEAM-甲", dashboard.TeamCode);
        Assert.Equal("on-duty", dashboard.TeamResolution);
    }

    [Fact]
    public async Task Manual_Selection_Outside_Shift_Window_Should_Stay_Manual()
    {
        // 12:00 不在任何班次内（甲班 8-16 与乙班 16-24 之间的空档属于另一天场景——
        // 这里甲班班次是 13:00-16:00，12:00 在班次开始之前）：手动选中也不算在岗。
        DateOnly today = new(2026, 11, 3);

        IShiftSchedulingService service = ServiceWithRows(
            Row("TEAM-甲", today, new TimeOnly(13, 0), new TimeOnly(16, 0)));
        DeterministicEnergyMetricService energy = new(service, FixedClock(12));

        EnergyDashboardDto dashboard = await energy.GetDashboardAsync(teamCode: "TEAM-甲");

        Assert.Equal("TEAM-甲", dashboard.TeamCode);
        Assert.Equal("manual", dashboard.TeamResolution);
    }

    [Fact]
    public async Task Manual_Selection_Of_RestDay_Team_Should_Stay_Manual()
    {
        // 当日轮休的班组即使被手动选中，也不得标成在岗。
        DateOnly today = new(2026, 11, 3);

        IShiftSchedulingService service = ServiceWithRows(
            Row("TEAM-甲", today, new TimeOnly(8, 0), new TimeOnly(16, 0), true));
        DeterministicEnergyMetricService energy = new(service, FixedClock(12));

        EnergyDashboardDto dashboard = await energy.GetDashboardAsync(teamCode: "TEAM-甲");

        Assert.Equal("TEAM-甲", dashboard.TeamCode);
        Assert.Equal("manual", dashboard.TeamResolution);
    }

    /// <summary>
    /// 固定时钟，避免真实时间导致测试结果随运行时刻漂移。
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now.ToUniversalTime();
        }
    }

    /// <summary>
    /// 只实现排班行查询的桩服务：仪表板解析只依赖 GetAllRowsAsync。
    /// </summary>
    private sealed class StubShiftSchedulingService(IReadOnlyCollection<ShiftScheduleRowDto> rows)
        : IShiftSchedulingService
    {
        public Task<ShiftScheduleImportResultDto> ImportAsync(
            string fileName, Stream stream, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("测试桩不支持导入。");
        }

        public Task<bool> ClearAsync()
        {
            throw new NotSupportedException("测试桩不支持清空。");
        }

        public Task<ShiftScheduleStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("测试桩不支持状态查询。");
        }

        public Task<IReadOnlyCollection<DailyShiftScheduleDto>> GetCalendarAsync(
            DateOnly from, DateOnly to, string? teamCode = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("测试桩不支持日历查询。");
        }

        public Task<TeamShiftScheduleDto> GetTeamScheduleAsync(
            string teamCode, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("测试桩不支持班组查询。");
        }

        public Task<IReadOnlyCollection<ShiftScheduleRowDto>> GetAllRowsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(rows);
        }
    }
}
