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
        var days = Math.Clamp(trendDays, 1, 60);

        IReadOnlyCollection<ShiftScheduleRowDto> scheduleRows =
            await shiftSchedulingService.GetAllRowsAsync(cancellationToken);

        ShiftTeamDto[] teams = ResolveTeams(scheduleRows);

        if (teams.Length == 0)
        {
            // 尚未导入排班表时使用默认四班组，保证页面可用。
            teams =
            [
                new ShiftTeamDto("TEAM-甲", "甲班"),
                new ShiftTeamDto("TEAM-乙", "乙班"),
                new ShiftTeamDto("TEAM-丙", "丙班"),
                new ShiftTeamDto("TEAM-丁", "丁班")
            ];
        }

        // 显式指定班组时按指定班组展示；未指定时才推断当前在岗班组。
        ShiftTeamDto current = string.IsNullOrWhiteSpace(teamCode)
            ? ResolveCurrentTeam(teams, today, timeProvider.GetLocalNow().TimeOfDay, scheduleRows)
            : teams.FirstOrDefault(team =>
                  string.Equals(team.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase))
              ?? new ShiftTeamDto(teamCode, ResolveTeamName(teamCode));

        DateOnly rangeStart = today.AddDays(-(days - 1));

        EnergyTrendDto[] trends = BuildTrends(current, rangeStart, today);

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
        var total = MetricCatalog
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
            trends);
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

        (DateOnly start, DateOnly end, var normalized) = NormalizeRange(granularity, rangeStart, rangeEnd);

        EnergyTrendDto[] trends = BuildTrends(new ShiftTeamDto(teamCode, ResolveTeamName(teamCode)), start, end);

        List<EnergyMetricValueDto> detailRows = [];
        foreach (EnergyTrendDto trend in trends)
        {
            foreach (EnergyTrendPointDto point in trend.Points)
            {
                detailRows.Add(new EnergyMetricValueDto(
                    teamCode,
                    ResolveTeamName(teamCode),
                    trend.MetricCode,
                    trend.MetricName,
                    trend.Unit,
                    point.BucketStart,
                    point.BucketStart,
                    point.Value));
            }
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

        (DateOnly start, DateOnly end, var normalized) = NormalizeRange(granularity, referenceDate, referenceDate);
        var label = ResolveBucketLabel(normalized, start, end);

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
                var sum = 0m;
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

        var total = MetricCatalog
            .Select((metric, index) =>
                ToDecimal(ComputeMetricRangeSum(team.TeamCode, metric.MetricCode, start, end)) * Coefficient(index))
            .Sum();

        ShiftScheduleRowDto[] teamRows = scheduleRows
            .Where(item =>
                string.Equals(item.TeamCode, team.TeamCode, StringComparison.OrdinalIgnoreCase) &&
                item.WorkDate >= start &&
                item.WorkDate <= end)
            .ToArray();

        var shiftCount = teamRows.Count(item => !item.IsRestDay);

        return new TeamEnergyComparisonDto(
            team.TeamCode,
            team.TeamName,
            metricValues,
            Math.Round(total, 2),
            shiftCount,
            Math.Round(teamRows.Sum(item => item.DurationHours), 2));
    }

    private static EnergyTrendDto[] BuildTrends(ShiftTeamDto team, DateOnly start, DateOnly end)
    {
        List<DateOnly> buckets = EnumerateBuckets(start, end).ToList();
        var bucketLength = ResolveBucketLength(start, end);
        var points = new Dictionary<string, List<EnergyTrendPointDto>>(StringComparer.Ordinal);

        foreach (EnergyMetricDescriptorDto metric in MetricCatalog)
        {
            points[metric.MetricCode] = [];
        }

        foreach (DateOnly bucket in buckets)
        {
            foreach (EnergyMetricDescriptorDto metric in MetricCatalog)
            {
                var value = ComputeBucketValue(team.TeamCode, metric.MetricCode, bucket, bucketLength, end);
                points[metric.MetricCode]
                    .Add(new EnergyTrendPointDto(bucket, FormatBucketLabel(bucket, start, end), ToDecimal(value)));
            }
        }

        return MetricCatalog.Select((metric, index) =>
        {
            List<EnergyTrendPointDto> series = points[metric.MetricCode];
            var total = series.Sum(item => item.Value);
            var average = series.Count == 0 ? 0m : Math.Round(total / series.Count, 2);
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

        var half = series.Count / 2;
        var firstHalf = series.Take(half).Average(item => item.Value);
        var secondHalf = series.Skip(half).Average(item => item.Value);

        var delta = secondHalf - firstHalf;
        var threshold = Math.Max(1m, Math.Abs(firstHalf) * 0.02m);

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
        var index = Array.FindIndex(MetricCatalog, metric => metric.MetricCode == metricCode);
        if (index < 0)
        {
            index = 0;
        }

        var (baseValue, amplitude) = MetricProfiles[index];

        // 由班组、日期、指标共同派生的确定性噪声：不同组合形态各异，同一组合永远一致。
        var noise = (DeriveNoise(teamCode, metricCode, day) * 2d) - 1d;
        var dailyWave = Math.Sin((day.DayNumber * 0.7) + index) * amplitude * 0.25;

        return Math.Round(baseValue + dailyWave + (noise * amplitude), 2);
    }

    /// <summary>
    /// 基于 FNV-1a 与整数混合散列派生出 [0,1) 区间的稳定随机数。
    /// 刻意不使用 <c>Random</c>：随机数生成器的状态无法跨请求复现，会导致刷新后数值漂移。
    /// </summary>
    private static double DeriveNoise(string teamCode, string metricCode, DateOnly day)
    {
        var hash = 14695981039346656037UL;

        foreach (var ch in teamCode)
        {
            hash = (hash ^ ch) * 1099511628211UL;
        }

        foreach (var ch in metricCode)
        {
            hash = (hash ^ ch) * 1099511628211UL;
        }

        hash ^= (ulong) day.DayNumber * 2654435761UL;

        // 再做一次异或移位混淆，降低相邻日期噪声之间的相关性。
        hash ^= hash >> 33;
        hash *= 0xff51afd7ed558ccdUL;
        hash ^= hash >> 33;

        return (hash >> 11) / (double) (1UL << 53);
    }

    /// <summary>
    /// 桶内数值为各天之和，使不同粒度下的总量口径一致。
    /// </summary>
    private static double ComputeBucketValue(
        string teamCode,
        string metricCode,
        DateOnly bucket,
        int bucketLength,
        DateOnly rangeEnd)
    {
        // 末桶不足一整桶宽时截断到区间末尾。
        DateOnly bucketEnd = bucket.AddDays(bucketLength - 1);
        if (bucketEnd > rangeEnd)
        {
            bucketEnd = rangeEnd;
        }

        var sum = 0d;
        for (DateOnly day = bucket; day <= bucketEnd; day = day.AddDays(1))
        {
            sum += ComputeDailyValue(teamCode, metricCode, day);
        }

        return Math.Round(sum, 2);
    }

    private static double ComputeMetricRangeSum(string teamCode, string metricCode, DateOnly start, DateOnly end)
    {
        var sum = 0d;
        for (DateOnly day = start; day <= end; day = day.AddDays(1))
        {
            sum += ComputeDailyValue(teamCode, metricCode, day);
        }

        return sum;
    }

    private static DateOnly ResolveBucketEnd(DateOnly bucket, DateOnly rangeStart, DateOnly rangeEnd)
    {
        var spanDays = rangeEnd.DayNumber - rangeStart.DayNumber;
        var bucketLength = spanDays <= 30 ? 1 : spanDays <= 120 ? 7 : 30;

        DateOnly candidate = bucket.AddDays(bucketLength - 1);
        return candidate > rangeEnd ? rangeEnd : candidate;
    }

    private static IEnumerable<DateOnly> EnumerateBuckets(DateOnly start, DateOnly end)
    {
        var spanDays = end.DayNumber - start.DayNumber + 1;

        if (spanDays <= 0)
        {
            yield return start;
            yield break;
        }

        var bucketLength = ResolveBucketLength(start, end);
        DateOnly cursor = start;

        // 按步长整齐分桶：每个桶覆盖连续 bucketLength 天，桶内求和。
        // 早期版本用「等分跳过」的方式生成桶，会让最后一个桶吃掉整段区间，
        // 既破坏总量口径，也会在曲线上产生虚假的归零跌落。
        while (cursor <= end)
        {
            yield return cursor;

            DateOnly next = cursor.AddDays(bucketLength);
            if (next <= cursor)
            {
                yield break;
            }

            cursor = next;
        }
    }

    /// <summary>
    /// 依据区间长度选择桶宽，桶数始终受 <see cref="MaxBucketCount"/> 约束，
    /// 保证 X 轴标签数量不会多到挤成一团。
    /// </summary>
    private static int ResolveBucketLength(DateOnly start, DateOnly end)
    {
        var spanDays = end.DayNumber - start.DayNumber + 1;

        if (spanDays <= MaxBucketCount)
        {
            return 1;
        }

        return Math.Max(1, (int) Math.Ceiling(spanDays / (double) MaxBucketCount));
    }

    private static string FormatBucketLabel(DateOnly bucket, DateOnly rangeStart, DateOnly rangeEnd)
    {
        var spanDays = rangeEnd.DayNumber - rangeStart.DayNumber + 1;

        return spanDays switch
        {
            <= 1 => bucket.ToString("yyyy-MM-dd"),
            <= 45 => bucket.ToString("MM-dd"),
            <= 120 => $"{bucket:MM-dd} 周",
            _ => bucket.ToString("yyyy-MM")
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
        var normalized = granularity.ToLowerInvariant() switch
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

    private static ShiftTeamDto ResolveCurrentTeam(
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
            (long) Math.Round(item.DurationHours * TimeSpan.TicksPerHour));

        if (currentRow is not null)
        {
            return teams.FirstOrDefault(team =>
                       string.Equals(team.TeamCode, currentRow.TeamCode, StringComparison.OrdinalIgnoreCase))
                   ?? new ShiftTeamDto(currentRow.TeamCode, currentRow.TeamName);
        }

        // 当日轮休或尚未导入排班时，按轮转顺序推断应属班组，保证仪表板始终有归属。
        var offset = today.DayNumber % Math.Max(1, teams.Count);
        return teams.ElementAt(offset % teams.Count);
    }

    /// <summary>
    /// 班组编码反解为显示名称，是 <see cref="ShiftScheduleExcelParser.NormalizeTeamCode"/> 的逆运算。
    /// 「TEAM-甲」与「TEAM-甲班」都还原为「甲班」。
    /// </summary>
    private static string ResolveTeamName(string teamCode)
    {
        var name = teamCode.StartsWith("TEAM-", StringComparison.OrdinalIgnoreCase)
            ? teamCode[5..]
            : teamCode;

        return name.EndsWith('班') ? name : $"{name}班";
    }

    private static decimal ToDecimal(double value)
    {
        return (decimal) Math.Round(value, 2);
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
