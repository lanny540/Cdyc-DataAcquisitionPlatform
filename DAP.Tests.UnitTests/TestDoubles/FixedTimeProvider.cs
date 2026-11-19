namespace DAP.Tests.UnitTests.TestDoubles;

/// <summary>
/// 固定时间的 TimeProvider，避免测试随系统日期漂移。
///
/// 多个服务（登录锁定、审计时间戳、能耗统计周期）依赖当前时间，
/// 固定后才能断言「锁定 15 分钟」「账期归属 11 月」这类与时间相关的行为。
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary>
    /// 以固定日期构造（零偏移 UTC）。
    /// </summary>
    public FixedTimeProvider(DateOnly date)
        : this(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
    {
    }

    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }
}
