using DAP.Core.Shared.Contracts;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 定义班组原辅材料与能源消耗指标在服务端对外提供的业务能力。
/// </summary>
public interface IEnergyMetricService
{
    /// <summary>
    /// 获取能耗指标定义清单。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>指标定义集合。</returns>
    Task<IReadOnlyCollection<EnergyMetricDescriptorDto>> GetMetricsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前班组的能耗总览，含四项指标合计与各自趋势。
    /// </summary>
    /// <param name="trendDays">趋势回溯天数。</param>
    /// <param name="teamCode">指定班组，为空时按当前时刻推断在岗班组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>能耗总览数据。</returns>
    Task<EnergyDashboardDto> GetDashboardAsync(
        int trendDays = 7,
        string? teamCode = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定班组的历史指标趋势。
    /// </summary>
    /// <param name="teamCode">班组编码。</param>
    /// <param name="granularity">统计粒度，取值为 day、week 或 month。</param>
    /// <param name="rangeStart">区间起始自然日。</param>
    /// <param name="rangeEnd">区间结束自然日。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>历史指标数据。</returns>
    Task<EnergyHistoryDto> GetHistoryAsync(
        string teamCode,
        string granularity,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有班组在指定统计口径下的横向对比。
    /// </summary>
    /// <param name="granularity">统计粒度，取值为 day、week 或 month。</param>
    /// <param name="referenceDate">参考自然日，用于推导日、周、月区间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>班组横向对比数据。</returns>
    Task<EnergyComparisonDto> GetComparisonAsync(
        string granularity,
        DateOnly referenceDate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 提供基于确定性算法的能耗指标服务。
///
/// 现阶段指标尚未接入真实计量点，这里按「班组编码 + 自然日 + 指标编码」生成稳定值：
/// 同一输入永远得到同一输出，因此页面刷新、重新查询都不会出现数值抖动。
/// 接入真实数据源时，只需替换本实现中产生序列的部分。
/// </summary>
public sealed class DeterministicEnergyMetricService(
    IShiftSchedulingService shiftSchedulingService,
    TimeProvider timeProvider) : IEnergyMetricService
{
    private const int MaxTrendDays = 180;
    private const int MaxBucketCount = 400;

    private static readonly EnergyMetricDescriptorDto[] MetricCatalog =
    [
        new("RAW_MATERIAL_A", "原辅材料 A", "吨", "原辅材料"),
        new("RAW_MATERIAL_B", "原辅材料 B", "吨", "原辅材料"),
        new("RAW_MATERIAL_C", "原辅材料 C", "吨", "原辅材料"),
        new("ENERGY_ELECTRIC", "电力消耗", "kWh", "能源消耗")
    ];

    /// <summary>
    /// 各指标的基准日均值与波动幅度，用于生成曲线形态。
    /// </summary>
    private static readonly (double Base, double Amplitude)[] MetricProfiles =
    [
        (128d, 14d),
        (96d, 11d),
        (54d, 7d),
        (18400d, 2100d)
    ];

    /// <inheritdoc />
    public Task<IReadOnlyCollection<EnergyMetricDescriptorDto>> GetMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyCollection<EnergyMetricDescriptorDto>>(MetricCatalog);
    }

    /// <inheritdoc />
    public async Task<EnergyDashboardDto> GetDashboardAsync(
        int trendDays = 7,
        string? teamCode = null,
        CancellationToken cancellationToken = default)
    {
        DateOnly today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        int days = Math.Clamp(trendDays, 1, 60);

        IReadOnlyCollection<ShiftScheduleRowDto> scheduleRows =
            await shiftSchedulingService.GetAllRowsAsync(cancellationToken);

        ShiftTeamDto[] teams = ResolveTeams(scheduleRows);

        if (teams.Length == 0)
            // 尚未导入排班表时使用默认四班组，保证页面可用。
        {
            teams =
            [
                new ShiftTeamDto("TEAM-甲", "甲班"),
                new ShiftTeamDto("TEAM-乙", "乙班"),
                new ShiftTeamDto("TEAM-丙", "丙班"),
                new ShiftTeamDto("TEAM-丁", "丁班")
            ];
        }

        // 显式指定班组时按指定班组展示；未指定时才严格解析当前在岗班组。
        // 严格解析（docs/AUTH_DESIGN.md 6.6）：不命中就不给默认值——当天轮休、排班未导入
        // 都不能靠取模"猜"一个班组，车间终端会把默认值当成在岗事实。
        bool requestedTeam = !string.IsNullOrWhiteSpace(teamCode);

        ShiftTeamDto current;
        string resolution;

        if (requestedTeam)
        {
            current = teams.FirstOrDefault(team =>
                          string.Equals(team.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase))
                      ?? new ShiftTeamDto(teamCode!, ResolveTeamName(teamCode!));

            // 手动选中的班组若恰好处于在岗班次内，仍按在岗呈现：徽标语义是
            // 「该班组此刻是否在岗」，与选择方式无关——用户切回在岗班组
            // 不应看到"非在岗"（此前手动指定一律返回 manual，正是这个 bug）。
            resolution = IsTeamOnDuty(
                    current.TeamCode,
                    today,
                    timeProvider.GetLocalNow().TimeOfDay,
                    scheduleRows)
                ? "on-duty"
                : "manual";
        }
        else
        {
            ShiftTeamDto? onDuty =
                TryResolveOnDutyTeam(teams, today, timeProvider.GetLocalNow().TimeOfDay, scheduleRows);

            if (onDuty is not null)
            {
                current = onDuty;
                resolution = "on-duty";
            }
            else
            {
                // 不隐藏数据、只拒绝猜测默认值：回退到排序第一的班组供浏览，
                // 由页面按 resolution 显式提示「非在岗事实」。
                current = teams[0];
                resolution = scheduleRows.Count == 0 ? "no-schedule" : "rest-day";
            }
        }

        DateOnly rangeStart = today.AddDays(-(days - 1));

        EnergyTrendDto[] trends = BuildTrends(current, "day", rangeStart, today);

        EnergyMetricValueDto[] totals = trends
            .Select(trend => new EnergyMetricValueDto(
                current.TeamCode,
                current.TeamName,
                trend.MetricCode,
                trend.MetricName,
                trend.Unit,
                today,
                today,
                trend.Points.LastOrDefault()?.Value ?? 0m))
            .ToArray();

        // 综合能耗按各指标折算为标准煤系数后求和，便于跨指标比较。
        decimal total = MetricCatalog
            .Select((metric, index) => ToDecimal(ComputeDailyValue(current.TeamCode, metric.MetricCode, today)) *
                                       Coefficient(index))
            .Sum();

        ShiftScheduleRowDto? currentRow = scheduleRows.FirstOrDefault(item =>
            item.TeamCode == current.TeamCode && item.WorkDate == today && !item.IsRestDay);

        return new EnergyDashboardDto(
            current.TeamCode,
            current.TeamName,
            today,
            currentRow?.ShiftName ?? "当班",
            currentRow?.StartTime ?? TimeOnly.MinValue,
            currentRow?.EndTime ?? TimeOnly.MinValue,
            Math.Round(total, 2),
            "kgce",
            totals,
            trends,
            resolution);
    }

    /// <inheritdoc />
    public Task<EnergyHistoryDto> GetHistoryAsync(
        string teamCode,
        string granularity,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamCode))
        {
            teamCode = "TEAM-甲";
        }

        (DateOnly start, DateOnly end, string normalized) = NormalizeRange(granularity, rangeStart, rangeEnd);

        EnergyTrendDto[] trends = BuildTrends(
            new ShiftTeamDto(teamCode, ResolveTeamName(teamCode)), normalized, start, end);

        List<EnergyMetricValueDto> detailRows = [];
        foreach (EnergyTrendDto trend in trends)
        foreach (EnergyTrendPointDto point in trend.Points)
        {
            detailRows.Add(new EnergyMetricValueDto(
                teamCode,
                ResolveTeamName(teamCode),
                trend.MetricCode,
                trend.MetricName,
                trend.Unit,
                point.BucketStart,
                point.BucketEnd ?? point.BucketStart,
                point.Value));
        }

        return Task.FromResult(new EnergyHistoryDto(
            teamCode,
            ResolveTeamName(teamCode),
            normalized,
            start,
            end,
            MetricCatalog,
            trends,
            detailRows));
    }

    /// <inheritdoc />
    public async Task<EnergyComparisonDto> GetComparisonAsync(
        string granularity,
        DateOnly referenceDate,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ShiftScheduleRowDto> scheduleRows =
            await shiftSchedulingService.GetAllRowsAsync(cancellationToken);

        ShiftTeamDto[] teams = ResolveTeams(scheduleRows);

        if (teams.Length == 0)
        {
            teams =
            [
                new ShiftTeamDto("TEAM-甲", "甲班"),
                new ShiftTeamDto("TEAM-乙", "乙班"),
                new ShiftTeamDto("TEAM-丙", "丙班"),
                new ShiftTeamDto("TEAM-丁", "丁班")
            ];
        }

        (DateOnly start, DateOnly end, string normalized) = NormalizeRange(granularity, referenceDate, referenceDate);

        // 周口径对比整个自然周（周一为一周起点）：此前参考日当天与周口径得到同一天，
        // 「按周对比」实际退化为单日对比。月口径已由 NormalizeRange 对齐到自然月。
        if (normalized == "week")
        {
            DateOnly weekStart = GetWeekStart(referenceDate);
            start = weekStart;
            end = weekStart.AddDays(6);
        }

        string label = ResolveBucketLabel(normalized, start, end);

        TeamEnergyComparisonDto[] comparisons = teams
            .Select(team => BuildComparison(team, start, end, scheduleRows))
            .ToArray();

        return new EnergyComparisonDto(normalized, start, end, label, MetricCatalog, comparisons);
    }

    private TeamEnergyComparisonDto BuildComparison(
        ShiftTeamDto team,
        DateOnly start,
        DateOnly end,
        IReadOnlyCollection<ShiftScheduleRowDto> scheduleRows)
    {
        EnergyMetricValueDto[] metricValues = MetricCatalog
            .Select((metric, index) =>
            {
                decimal sum = 0m;
                for (DateOnly day = start; day <= end; day = day.AddDays(1))
                {
                    sum += ToDecimal(ComputeDailyValue(team.TeamCode, metric.MetricCode, day));
                }

                return new EnergyMetricValueDto(
                    team.TeamCode,
                    team.TeamName,
                    metric.MetricCode,
                    metric.MetricName,
                    metric.Unit,
                    start,
                    end,
                    Math.Round(sum, 2));
            })
            .ToArray();

        decimal total = MetricCatalog
            .Select((metric, index) =>
                ToDecimal(ComputeMetricRangeSum(team.TeamCode, metric.MetricCode, start, end)) * Coefficient(index))
            .Sum();

        ShiftScheduleRowDto[] teamRows = scheduleRows
            .Where(item =>
                string.Equals(item.TeamCode, team.TeamCode, StringComparison.OrdinalIgnoreCase) &&
                item.WorkDate >= start &&
                item.WorkDate <= end)
            .ToArray();

        int shiftCount = teamRows.Count(item => !item.IsRestDay);

        return new TeamEnergyComparisonDto(
            team.TeamCode,
            team.TeamName,
            metricValues,
            Math.Round(total, 2),
            shiftCount,
            Math.Round(teamRows.Sum(item => item.DurationHours), 2));
    }

    private static EnergyTrendDto[] BuildTrends(ShiftTeamDto team, string granularity, DateOnly start, DateOnly end)
    {
        List<(DateOnly Start, DateOnly End)> buckets = EnumerateBuckets(granularity, start, end);
        Dictionary<string, List<EnergyTrendPointDto>> points = new(StringComparer.Ordinal);

        foreach (EnergyMetricDescriptorDto metric in MetricCatalog)
        {
            points[metric.MetricCode] = [];
        }

        foreach ((DateOnly bucketStart, DateOnly bucketEnd) in buckets)
        foreach (EnergyMetricDescriptorDto metric in MetricCatalog)
        {
            double value = ComputeBucketValue(team.TeamCode, metric.MetricCode, bucketStart, bucketEnd);
            points[metric.MetricCode]
                .Add(new EnergyTrendPointDto(
                    bucketStart,
                    FormatBucketLabel(granularity, bucketStart, bucketEnd),
                    ToDecimal(value),
                    bucketEnd));
        }

        return MetricCatalog.Select((metric, index) =>
        {
            List<EnergyTrendPointDto> series = points[metric.MetricCode];
            decimal total = series.Sum(item => item.Value);
            decimal average = series.Count == 0 ? 0m : Math.Round(total / series.Count, 2);
            EnergyTrendPointDto? peak = series.OrderByDescending(item => item.Value).FirstOrDefault();

            return new EnergyTrendDto(
                metric.MetricCode,
                metric.MetricName,
                metric.Unit,
                series,
                Math.Round(total, 2),
                average,
                peak?.Value ?? 0m,
                peak?.BucketStart,
                ResolveTrendDirection(series));
        }).ToArray();
    }

    private static string ResolveTrendDirection(IReadOnlyCollection<EnergyTrendPointDto> series)
    {
        if (series.Count < 2)
        {
            return "flat";
        }

        int half = series.Count / 2;
        decimal firstHalf = series.Take(half).Average(item => item.Value);
        decimal secondHalf = series.Skip(half).Average(item => item.Value);

        decimal delta = secondHalf - firstHalf;
        decimal threshold = Math.Max(1m, Math.Abs(firstHalf) * 0.02m);

        if (delta > threshold)
        {
            return "up";
        }

        return delta < -threshold ? "down" : "flat";
    }

    /// <summary>
    /// 生成单日指标值。核心是取哈希而非随机数，保证同一班组同一天的值恒定。
    /// </summary>
    private static double ComputeDailyValue(string teamCode, string metricCode, DateOnly day)
    {
        int index = Array.FindIndex(MetricCatalog, metric => metric.MetricCode == metricCode);
        if (index < 0)
        {
            index = 0;
        }

        (double baseValue, double amplitude) = MetricProfiles[index];

        // 由班组、日期、指标共同派生的确定性噪声：不同组合形态各异，同一组合永远一致。
        double noise = (DeriveNoise(teamCode, metricCode, day) * 2d) - 1d;
        double dailyWave = Math.Sin((day.DayNumber * 0.7) + index) * amplitude * 0.25;

        return Math.Round(baseValue + dailyWave + (noise * amplitude), 2);
    }

    /// <summary>
    /// 基于 FNV-1a 与整数混合散列派生出 [0,1) 区间的稳定随机数。
    /// 刻意不使用 <c>Random</c>：随机数生成器的状态无法跨请求复现，会导致刷新后数值漂移。
    /// </summary>
    private static double DeriveNoise(string teamCode, string metricCode, DateOnly day)
    {
        ulong hash = 14695981039346656037UL;

        foreach (char ch in teamCode)
        {
            hash = (hash ^ ch) * 1099511628211UL;
        }

        foreach (char ch in metricCode)
        {
            hash = (hash ^ ch) * 1099511628211UL;
        }

        hash ^= (ulong)day.DayNumber * 2654435761UL;

        // 再做一次异或移位混淆，降低相邻日期噪声之间的相关性。
        hash ^= hash >> 33;
        hash *= 0xff51afd7ed558ccdUL;
        hash ^= hash >> 33;

        return (hash >> 11) / (double)(1UL << 53);
    }

    /// <summary>
    /// 桶内数值为各天之和，使不同粒度下的总量口径一致。
    /// </summary>
    private static double ComputeBucketValue(
        string teamCode,
        string metricCode,
        DateOnly bucketStart,
        DateOnly bucketEnd)
    {
        double sum = 0d;
        for (DateOnly day = bucketStart; day <= bucketEnd; day = day.AddDays(1))
        {
            sum += ComputeDailyValue(teamCode, metricCode, day);
        }

        return Math.Round(sum, 2);
    }

    private static double ComputeMetricRangeSum(string teamCode, string metricCode, DateOnly start, DateOnly end)
    {
        double sum = 0d;
        for (DateOnly day = start; day <= end; day = day.AddDays(1))
        {
            sum += ComputeDailyValue(teamCode, metricCode, day);
        }

        return sum;
    }

    /// <summary>
    /// 按统计口径把区间切成连续的桶，返回每个桶的起止日期。
    /// 按日为单日桶；按周为自区间起点起连续 7 天的桶（末桶不足一周时截断）；
    /// 按月为自然月桶。任何口径下桶内都是「逐日求和」，总量口径彼此一致。
    /// </summary>
    private static List<(DateOnly Start, DateOnly End)> EnumerateBuckets(
        string granularity, DateOnly start, DateOnly end)
    {
        List<(DateOnly Start, DateOnly End)> buckets = [];

        if (end < start)
        {
            buckets.Add((start, start));
            return buckets;
        }

        if (granularity == "month")
        {
            // 自然月桶：月边界由 NormalizeRange 对齐，这里按月步进并在区间末尾截断。
            for (DateOnly cursor = new(start.Year, start.Month, 1); cursor <= end; cursor = cursor.AddMonths(1))
            {
                DateOnly monthEnd = new(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
                buckets.Add((cursor, monthEnd > end ? end : monthEnd));
            }

            return buckets;
        }

        int bucketLength = ResolveBucketLength(granularity, start, end);
        DateOnly current = start;

        // 按步长整齐分桶：每个桶覆盖连续 bucketLength 天，桶内求和。
        // 早期版本用「等分跳过」的方式生成桶，会让最后一个桶吃掉整段区间，
        // 既破坏总量口径，也会在曲线上产生虚假的归零跌落。
        while (current <= end)
        {
            DateOnly bucketEnd = current.AddDays(bucketLength - 1);
            buckets.Add((current, bucketEnd > end ? end : bucketEnd));

            DateOnly next = current.AddDays(bucketLength);
            if (next <= current)
            {
                break;
            }

            current = next;
        }

        return buckets;
    }

    /// <summary>
    /// 依据统计口径选择桶宽：按日为 1 天，按周为 7 天；区间过长时按粒度的整数倍加宽，
    /// 保证桶数始终受 <see cref="MaxBucketCount"/> 约束（周桶保持完整 7 天的倍数）。
    /// </summary>
    private static int ResolveBucketLength(string granularity, DateOnly start, DateOnly end)
    {
        int spanDays = end.DayNumber - start.DayNumber + 1;
        int naturalLength = granularity == "week" ? 7 : 1;
        int bucketCount = (int)Math.Ceiling(spanDays / (double)naturalLength);

        if (bucketCount <= MaxBucketCount)
        {
            return naturalLength;
        }

        int widenFactor = (int)Math.Ceiling(bucketCount / (double)MaxBucketCount);
        return naturalLength * widenFactor;
    }

    /// <summary>
    /// 横轴标签：按周显示「起~止」，按月显示年月，按日显示单日；
    /// 跨多日的加宽桶只标起点日（仅超长区间会出现）。
    /// </summary>
    private static string FormatBucketLabel(string granularity, DateOnly bucketStart, DateOnly bucketEnd)
    {
        return granularity switch
        {
            "week" => $"{bucketStart:MM-dd}~{bucketEnd:MM-dd}",
            "month" => bucketStart.ToString("yyyy-MM"),
            _ => bucketStart == bucketEnd
                ? bucketStart.ToString("yyyy-MM-dd")
                : bucketStart.ToString("MM-dd")
        };
    }

    private static string ResolveBucketLabel(string granularity, DateOnly start, DateOnly end)
    {
        return granularity switch
        {
            "week" => $"{start:yyyy-MM-dd} ~ {end:yyyy-MM-dd}",
            "month" => $"{start:yyyy-MM} 月",
            _ => start.ToString("yyyy-MM-dd")
        };
    }

    private static (DateOnly Start, DateOnly End, string Normalized) NormalizeRange(
        string granularity,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        string normalized = granularity.ToLowerInvariant() switch
        {
            "week" => "week",
            "month" => "month",
            _ => "day"
        };

        DateOnly start = rangeStart;
        DateOnly end = rangeEnd;

        if (end < start)
        {
            (start, end) = (end, start);
        }

        return normalized switch
        {
            "week" => (start, end, normalized),
            "month" => (
                // 月粒度对齐到自然月起点与终点，合计口径与当月一致。
                new DateOnly(start.Year, start.Month, 1),
                new DateOnly(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month)),
                normalized),
            _ => (
                // 日粒度尊重调用方给定的区间。早期实现会把跨月的区间强行扩成两个整月，
                // 导致 30 天的查询变成 61 天，数据点翻倍且总量口径与用户所选范围不符。
                // 这里只在区间过长时收敛桶宽，而不是悄悄放大区间。
                start,
                end,
                normalized)
        };
    }

    private static ShiftTeamDto[] ResolveTeams(
        IReadOnlyCollection<ShiftScheduleRowDto> scheduleRows)
    {
        return scheduleRows
            .GroupBy(item => item.TeamCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ShiftTeamDto(group.Key, group.First().TeamName))
            .OrderBy(item => item.TeamName, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 求参考日所在自然周的第一天（周一为一周起点）。
    /// </summary>
    private static DateOnly GetWeekStart(DateOnly date)
    {
        // DayOfWeek：Sunday=0、Monday=1…Saturday=6；周日按一周之末处理，回退 6 天。
        int offset = date.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)date.DayOfWeek - 1;
        return date.AddDays(-offset);
    }

    /// <summary>
    /// 判断指定班组当前时刻是否处于在岗班次内：命中今日排班、非轮休、
    /// 且当前时刻落在该班组的班次窗口（起止时间）内。
    /// </summary>
    private static bool IsTeamOnDuty(
        string teamCode,
        DateOnly today,
        TimeSpan now,
        IReadOnlyCollection<ShiftScheduleRowDto> scheduleRows)
    {
        return scheduleRows.Any(item =>
            string.Equals(item.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase) &&
            item.WorkDate == today &&
            !item.IsRestDay &&
            item.StartTime.Ticks <= now.Ticks &&
            now.Ticks < item.StartTime.Ticks +
            (long)Math.Round(item.DurationHours * TimeSpan.TicksPerHour));
    }

    /// <summary>
    /// 严格解析当前在岗班组：命中当天排班且当前时刻在班次内才返回；
    /// 当天轮休、无匹配班次、排班未导入一律返回 null，绝不取模猜测
    /// （docs/AUTH_DESIGN.md 6.6：可以给默认值，但不能"猜"）。
    /// </summary>
    private static ShiftTeamDto? TryResolveOnDutyTeam(
        IReadOnlyCollection<ShiftTeamDto> teams,
        DateOnly today,
        TimeSpan now,
        IReadOnlyCollection<ShiftScheduleRowDto> scheduleRows)
    {
        ShiftScheduleRowDto? currentRow = scheduleRows.FirstOrDefault(item =>
            item.WorkDate == today &&
            !item.IsRestDay &&
            item.StartTime.Ticks <= now.Ticks &&
            now.Ticks < item.StartTime.Ticks +
            (long)Math.Round(item.DurationHours * TimeSpan.TicksPerHour));

        if (currentRow is null)
        {
            return null;
        }

        return teams.FirstOrDefault(team =>
                   string.Equals(team.TeamCode, currentRow.TeamCode, StringComparison.OrdinalIgnoreCase))
               ?? new ShiftTeamDto(currentRow.TeamCode, currentRow.TeamName);
    }

    /// <summary>
    /// 班组编码反解为显示名称，是 <see cref="ShiftScheduleExcelParser.NormalizeTeamCode"/> 的逆运算。
    /// 「TEAM-甲」与「TEAM-甲班」都还原为「甲班」。
    /// </summary>
    private static string ResolveTeamName(string teamCode)
    {
        string name = teamCode.StartsWith("TEAM-", StringComparison.OrdinalIgnoreCase)
            ? teamCode[5..]
            : teamCode;

        return name.EndsWith('班') ? name : $"{name}班";
    }

    private static decimal ToDecimal(double value)
    {
        return (decimal)Math.Round(value, 2);
    }

    /// <summary>
    /// 各指标折算为标准煤的系数，用于汇总综合能耗。
    /// </summary>
    private static decimal Coefficient(int index)
    {
        return index switch
        {
            3 => 0.0012m,
            _ => 0.7143m
        };
    }
}
