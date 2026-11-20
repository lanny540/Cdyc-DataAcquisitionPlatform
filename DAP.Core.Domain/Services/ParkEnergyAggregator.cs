namespace DAP.Core.Domain.Services;

/// <summary>
/// 一个统计期间的起止日（含首尾）。
/// </summary>
/// <param name="Start">起始日，含。</param>
/// <param name="EndInclusive">结束日，含。</param>
public sealed record ParkEnergyPeriod(DateOnly Start, DateOnly EndInclusive)
{
    /// <summary>
    /// 取得期间起点时刻。
    /// </summary>
    /// <param name="offset">园区所在时区相对 UTC 的偏移。</param>
    /// <returns>起点时刻。</returns>
    public DateTimeOffset StartInstant(TimeSpan offset)
    {
        return new DateTimeOffset(Start.ToDateTime(TimeOnly.MinValue), offset);
    }

    /// <summary>
    /// 取得期间终点时刻（不含），即结束日的次日零点。
    /// </summary>
    /// <param name="offset">园区所在时区相对 UTC 的偏移。</param>
    /// <returns>终点时刻。</returns>
    public DateTimeOffset EndInstant(TimeSpan offset)
    {
        return new DateTimeOffset(EndInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue), offset);
    }
}

/// <summary>
/// 参与能耗聚合的计量点。
///
/// 与实体分开，是为了让聚合逻辑不依赖 EF：用量差分、折标、组织上卷
/// 这几条口径是报表出错的高发区，必须能在没有数据库的情况下逐条断言。
/// </summary>
/// <param name="Id">计量点标识。</param>
/// <param name="Code">计量点编码。</param>
/// <param name="Name">计量点名称。</param>
/// <param name="MediumCode">介质编码。</param>
/// <param name="MediumName">介质名称。</param>
/// <param name="Unit">计量单位。</param>
/// <param name="IsPrimaryEnergy">是否为一次能源，只有一次能源计入综合能耗。</param>
/// <param name="CoalFactor">折标煤系数，单位 kgce/计量单位；未知时为空。</param>
/// <param name="IsCumulative">读数是否为累计示数。</param>
/// <param name="Ratio">倍率。</param>
/// <param name="OrgUnitId">所属组织节点标识。</param>
/// <param name="OrgUnitName">所属组织节点名称。</param>
/// <param name="ParentMeterId">上级计量点标识。</param>
public sealed record ParkEnergyMeterInput(
    Guid Id,
    string Code,
    string Name,
    string MediumCode,
    string MediumName,
    string Unit,
    bool IsPrimaryEnergy,
    decimal? CoalFactor,
    bool IsCumulative,
    decimal Ratio,
    Guid OrgUnitId,
    string OrgUnitName,
    Guid? ParentMeterId = null);

/// <summary>
/// 参与聚合的一条读数。
/// </summary>
/// <param name="MeterId">计量点标识。</param>
/// <param name="ReadAt">抄见时刻。</param>
/// <param name="Value">读数原始值，未乘倍率。</param>
public sealed record ParkEnergyReadingInput(Guid MeterId, DateTimeOffset ReadAt, decimal Value);

/// <summary>
/// 一种介质在统计期间内的用量。
/// </summary>
/// <param name="Code">介质编码。</param>
/// <param name="Name">介质名称。</param>
/// <param name="Unit">计量单位。</param>
/// <param name="Quantity">实物量。</param>
/// <param name="CoalEquivalentTce">折标量，单位 tce；缺折标系数时为空。</param>
/// <param name="IsPrimaryEnergy">是否为一次能源。</param>
/// <param name="MeterCount">参与汇总的计量点数量。</param>
public sealed record ParkEnergyMediumUsage(
    string Code,
    string Name,
    string Unit,
    decimal Quantity,
    decimal? CoalEquivalentTce,
    bool IsPrimaryEnergy,
    int MeterCount);

/// <summary>
/// 一个组织节点在统计期间内的用量。
/// </summary>
/// <param name="OrgUnitId">组织节点标识。</param>
/// <param name="OrgUnitName">组织节点名称。</param>
/// <param name="CoalEquivalentTce">折标量，单位 tce；该节点下全部介质都缺系数时为空。</param>
/// <param name="Quantity">该节点下各介质实物量的合计，仅用于展示量级，不可跨介质比较。</param>
public sealed record ParkEnergyOrgUsage(
    Guid OrgUnitId,
    string OrgUnitName,
    decimal? CoalEquivalentTce,
    decimal Quantity);

/// <summary>
/// 一个统计期间的聚合结果。
/// </summary>
/// <param name="HasReadings">期间内是否读到任何一条读数。</param>
/// <param name="DataThrough">已落库的最新读数时刻。</param>
/// <param name="TotalTce">综合能耗，单位 tce，只汇总一次能源。</param>
/// <param name="Mediums">按介质汇总，按折标量降序（无折标量的排最后）。</param>
/// <param name="Orgs">按组织汇总，按折标量降序。</param>
/// <param name="Warnings">口径提示与数据异常。</param>
public sealed record ParkEnergyAggregate(
    bool HasReadings,
    DateTimeOffset? DataThrough,
    decimal TotalTce,
    IReadOnlyList<ParkEnergyMediumUsage> Mediums,
    IReadOnlyList<ParkEnergyOrgUsage> Orgs,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// 空结果。
    /// </summary>
    public static ParkEnergyAggregate Empty { get; } = new(false, null, 0m, [], [], []);
}

/// <summary>
/// 园区能源用量聚合器。
///
/// 三条口径在这里一次性定死，页面与报表都只消费这里的结果：
///
/// **一、累计表必须差分。** 电表给的是示数，期间用量等于末次示数减基线示数。
/// 基线取「期间起点之前最后一次抄见」，不是期间内第一条——少了这条，
/// 每月 1 日的报表会把整月 1 号之前的用量全部丢掉或者重复计入。
///
/// **二、期间内只有一条累计读数时，用量未知而不是 0。** 只抄了一次表，
/// 减出来必然是 0，但真实用量可能很大。这种情况记入「无法计算」并给出提示，
/// 页面上显示为数据不足，不能显示为 0。
///
/// **三、只有一次能源进综合能耗。** 压缩空气与空调制冷量由电或蒸汽转换而来，
/// 一并折标会算两遍。它们单独汇总，作为分项展示。
/// </summary>
public static class ParkEnergyAggregator
{
    /// <summary>
    /// 汇总一个统计期间内各介质与各组织的用量。
    /// </summary>
    /// <param name="meters">计量点。</param>
    /// <param name="readings">
    /// 读数。可以包含期间之外的记录——累计表需要期间起点之前的最后一条作为基线，
    /// 由本方法自行筛选。
    /// </param>
    /// <param name="period">统计期间。</param>
    /// <param name="offset">园区所在时区相对 UTC 的偏移。</param>
    /// <returns>聚合结果。</returns>
    public static ParkEnergyAggregate Aggregate(
        IReadOnlyCollection<ParkEnergyMeterInput> meters,
        IReadOnlyCollection<ParkEnergyReadingInput> readings,
        ParkEnergyPeriod period,
        TimeSpan offset)
    {
        ArgumentNullException.ThrowIfNull(meters);
        ArgumentNullException.ThrowIfNull(readings);

        DateTimeOffset start = period.StartInstant(offset);
        DateTimeOffset end = period.EndInstant(offset);

        List<string> warnings = new();
        Dictionary<Guid, ParkEnergyReadingInput[]> byMeter = readings
            .GroupBy(reading => reading.MeterId)
            .ToDictionary(group => group.Key, group => group.OrderBy(reading => reading.ReadAt).ToArray());

        DateTimeOffset? dataThrough = readings.Count == 0
            ? null
            : readings.Max(reading => reading.ReadAt).ToOffset(offset);

        Dictionary<string, MediumAccumulator> mediumTotals = new(StringComparer.Ordinal);
        Dictionary<Guid, OrgAccumulator> orgTotals = new();
        HashSet<Guid> meterIds = meters.Select(meter => meter.Id).ToHashSet();
        bool hasReadings = readings.Any(reading =>
            meterIds.Contains(reading.MeterId) && reading.ReadAt >= start && reading.ReadAt < end);
        List<string> shortMeters = new();

        HashSet<Guid> invalidMeters = FindInvalidMeterRelationships(meters);
        if (invalidMeters.Count > 0)
        {
            warnings.Add($"有 {invalidMeters.Count} 个计量点的总分表关系存在循环或介质不一致，已排除相关计量树，请检查台账。");
        }

        ParkEnergyMeterInput[] validMeters = meters.Where(meter => !invalidMeters.Contains(meter.Id)).ToArray();
        Dictionary<Guid, ParkEnergyMeterInput> meterMap = validMeters.ToDictionary(meter => meter.Id);
        Dictionary<Guid, ParkEnergyMeterInput[]> children = validMeters
            .Where(meter => meter.ParentMeterId.HasValue && meterMap.ContainsKey(meter.ParentMeterId.Value))
            .GroupBy(meter => meter.ParentMeterId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        Dictionary<Guid, decimal> quantities = new();

        foreach (ParkEnergyMeterInput meter in validMeters)
        {
            if (!byMeter.TryGetValue(meter.Id, out ParkEnergyReadingInput[]? series) || series.Length == 0)
            {
                continue;
            }

            decimal? quantity = ResolveQuantity(meter, series, start, end, shortMeters, warnings);
            if (quantity is null)
            {
                continue;
            }

            quantities[meter.Id] = quantity.Value;
        }

        foreach (ParkEnergyMeterInput meter in validMeters.Where(meter =>
                     !meter.ParentMeterId.HasValue || !meterMap.ContainsKey(meter.ParentMeterId.Value)))
        {
            if (!quantities.TryGetValue(meter.Id, out decimal quantity))
            {
                if (children.ContainsKey(meter.Id))
                {
                    warnings.Add($"{meter.Name}总表读数不足，所属分表未计入园区总量。");
                }

                continue;
            }

            decimal? coal = meter.CoalFactor is { } factor ? quantity * factor / 1000m : (decimal?)null;
            if (!mediumTotals.TryGetValue(meter.MediumCode, out MediumAccumulator? medium))
            {
                medium = new MediumAccumulator(
                    meter.MediumCode, meter.MediumName, meter.Unit, meter.IsPrimaryEnergy);
                mediumTotals[meter.MediumCode] = medium;
            }

            medium.Add(quantity, coal);
        }

        foreach (ParkEnergyMeterInput meter in validMeters)
        {
            if (!quantities.TryGetValue(meter.Id, out decimal quantity))
            {
                continue;
            }

            if (children.TryGetValue(meter.Id, out ParkEnergyMeterInput[]? directChildren))
            {
                if (directChildren.Any(child => !quantities.ContainsKey(child.Id)))
                {
                    warnings.Add($"{meter.Name}存在读数不足的分表，无法计算总分表差额。");
                    continue;
                }

                decimal difference = quantity - directChildren.Sum(child => quantities[child.Id]);
                if (difference < 0)
                {
                    warnings.Add($"{meter.Name}的分表用量超过总表，请检查计量数据。");
                    continue;
                }

                AddOrg(Guid.Empty, "损耗", difference, meter);
            }
            else
            {
                AddOrg(meter.OrgUnitId, meter.OrgUnitName, quantity, meter);
            }
        }

        void AddOrg(Guid id, string name, decimal quantity, ParkEnergyMeterInput meter)
        {
            if (!orgTotals.TryGetValue(id, out OrgAccumulator? org))
            {
                org = new OrgAccumulator(id, name);
                orgTotals[id] = org;
            }

            decimal? coal = meter.CoalFactor is { } factor ? quantity * factor / 1000m : (decimal?)null;
            org.Add(quantity, coal);
        }

        if (shortMeters.Count > 0)
        {
            warnings.Add(
                $"{string.Join("、", shortMeters.Take(3))}{(shortMeters.Count > 3 ? $" 等 {shortMeters.Count} 块表" : string.Empty)}"
                + "在统计期间内读数不足两条且无期间前基线，用量无法计算，未计入汇总。");
        }

        ParkEnergyMediumUsage[] mediums = mediumTotals.Values
            .Select(accumulator => accumulator.ToUsage())
            .OrderByDescending(usage => usage.CoalEquivalentTce ?? -1m)
            .ThenBy(usage => usage.Code, StringComparer.Ordinal)
            .ToArray();

        decimal totalTce = mediums
            .Where(usage => usage.IsPrimaryEnergy && usage.CoalEquivalentTce.HasValue)
            .Sum(usage => usage.CoalEquivalentTce!.Value);

        string[] missingFactor = mediums
            .Where(usage => usage.IsPrimaryEnergy && usage.CoalEquivalentTce is null)
            .Select(usage => usage.Name)
            .ToArray();

        if (missingFactor.Length > 0)
        {
            warnings.Add(
                $"{string.Join("、", missingFactor)}尚未配置折标煤系数，未计入综合能耗；请在介质字典中按实际焓值补齐。");
        }

        ParkEnergyOrgUsage[] orgs = orgTotals.Values
            .Select(accumulator => accumulator.ToUsage())
            .OrderByDescending(usage => usage.CoalEquivalentTce ?? -1m)
            .ThenBy(usage => usage.OrgUnitName, StringComparer.Ordinal)
            .ToArray();

        return new ParkEnergyAggregate(hasReadings, dataThrough, totalTce, mediums, orgs, warnings);
    }

    /// <summary>
    /// 构造某一天的小时级电负荷。
    ///
    /// 负荷曲线看的是「那一天」，与统计期间不是一个区间：月度报表的期间是整月，
    /// 但负荷曲线只画最近一个有数据的自然日，两者混用会让横轴莫名其妙地变成 720 小时。
    /// </summary>
    /// <param name="meters">计量点，只取介质为 ELEC 的表。</param>
    /// <param name="readings">读数。</param>
    /// <param name="day">自然日。</param>
    /// <param name="offset">园区所在时区相对 UTC 的偏移。</param>
    /// <returns>24 个元素的数组；无数据的时段为空。</returns>
    public static decimal?[] BuildHourlyLoad(
        IReadOnlyCollection<ParkEnergyMeterInput> meters,
        IReadOnlyCollection<ParkEnergyReadingInput> readings,
        DateOnly day,
        TimeSpan offset)
    {
        ArgumentNullException.ThrowIfNull(meters);
        ArgumentNullException.ThrowIfNull(readings);

        DateTimeOffset dayStart = new(day.ToDateTime(TimeOnly.MinValue), offset);
        DateTimeOffset dayEnd = dayStart.AddDays(1);

        double[] buckets = new double[24];
        bool[] touched = new bool[24];

        HashSet<Guid> invalidMeters = FindInvalidMeterRelationships(meters);
        Dictionary<Guid, ParkEnergyMeterInput> electricMeters = meters
            .Where(meter => !invalidMeters.Contains(meter.Id))
            .Where(meter => string.Equals(meter.MediumCode, "ELEC", StringComparison.Ordinal))
            .ToDictionary(meter => meter.Id);
        HashSet<Guid> rootMeters = electricMeters.Values
            .Where(meter => !meter.ParentMeterId.HasValue || !electricMeters.ContainsKey(meter.ParentMeterId.Value))
            .Select(meter => meter.Id)
            .ToHashSet();

        foreach (IGrouping<Guid, ParkEnergyReadingInput> group in readings.GroupBy(reading => reading.MeterId))
        {
            if (!rootMeters.Contains(group.Key) ||
                !electricMeters.TryGetValue(group.Key, out ParkEnergyMeterInput? meter))
            {
                continue;
            }

            ParkEnergyReadingInput[] series = group.OrderBy(reading => reading.ReadAt).ToArray();

            for (int index = 1; index < series.Length; index++)
            {
                ParkEnergyReadingInput previous = series[index - 1];
                ParkEnergyReadingInput current = series[index];

                decimal delta = meter.IsCumulative
                    ? (current.Value - previous.Value) * meter.Ratio
                    : current.Value * meter.Ratio;

                if (delta <= 0)
                {
                    continue;
                }

                Distribute(delta, previous.ReadAt, current.ReadAt, dayStart, dayEnd, offset, buckets, touched);
            }

            // 非累计表的首条读数没有前一条可以定区间，按「截至该时刻的 1 小时」摊。
            if (!meter.IsCumulative && series.Length > 0)
            {
                ParkEnergyReadingInput first = series[0];
                decimal delta = first.Value * meter.Ratio;
                if (delta > 0)
                {
                    Distribute(delta, first.ReadAt.AddHours(-1), first.ReadAt, dayStart, dayEnd, offset, buckets,
                        touched);
                }
            }
        }

        decimal?[] result = new decimal?[24];
        for (int hour = 0; hour < 24; hour++)
        {
            if (touched[hour])
                // 该小时的电量除以 1 小时，即为该小时的平均功率。
            {
                result[hour] = Math.Round((decimal)buckets[hour], 1);
            }
        }

        return result;
    }

    /// <summary>
    /// 计算一块表在统计期间内的用量。
    /// </summary>
    private static decimal? ResolveQuantity(
        ParkEnergyMeterInput meter,
        ParkEnergyReadingInput[] series,
        DateTimeOffset start,
        DateTimeOffset end,
        List<string> shortMeters,
        List<string> warnings)
    {
        ParkEnergyReadingInput[] inPeriod =
            series.Where(reading => reading.ReadAt >= start && reading.ReadAt < end).ToArray();

        if (inPeriod.Length == 0)
        {
            return null;
        }

        if (!meter.IsCumulative)
            // 非累计表约定：读数本身即本区间用量，直接累加，不做差分。
        {
            return inPeriod.Sum(reading => reading.Value) * meter.Ratio;
        }

        ParkEnergyReadingInput? baseline = series.LastOrDefault(reading => reading.ReadAt < start);
        ParkEnergyReadingInput[] measured = inPeriod;

        if (baseline is null)
        {
            if (inPeriod.Length < 2)
            {
                shortMeters.Add($"{meter.Name}");
                return null;
            }

            baseline = inPeriod[0];
            measured = inPeriod[1..];
        }

        ParkEnergyReadingInput last = measured.Length > 0 ? measured[^1] : baseline;
        decimal delta = (last.Value - baseline.Value) * meter.Ratio;

        if (delta < 0)
        {
            // 示数回退通常意味着换表或清零。按 0 计并给出提示，
            // 直接取绝对值会把一次换表变成一笔凭空的巨额能耗。
            warnings.Add($"{meter.Name}在统计期间内示数回退（可能换表或清零），本次按 0 计。");
            return 0m;
        }

        return delta;
    }

    private static HashSet<Guid> FindInvalidMeterRelationships(IReadOnlyCollection<ParkEnergyMeterInput> meters)
    {
        Dictionary<Guid, ParkEnergyMeterInput> byId = meters.ToDictionary(meter => meter.Id);
        HashSet<Guid> invalid = new();
        foreach (ParkEnergyMeterInput meter in meters)
        {
            HashSet<Guid> path = new();
            ParkEnergyMeterInput current = meter;
            while (true)
            {
                if (!path.Add(current.Id))
                {
                    invalid.UnionWith(path);
                    break;
                }

                if (current.ParentMeterId is not { } parentId ||
                    !byId.TryGetValue(parentId, out ParkEnergyMeterInput? parent))
                {
                    break;
                }

                if (!string.Equals(current.MediumCode, parent.MediumCode, StringComparison.Ordinal))
                {
                    path.Add(parent.Id);
                    invalid.UnionWith(path);
                    break;
                }

                current = parent;
            }
        }

        foreach (ParkEnergyMeterInput meter in meters)
        {
            HashSet<Guid> visited = new();
            ParkEnergyMeterInput current = meter;
            while (visited.Add(current.Id))
            {
                if (invalid.Contains(current.Id))
                {
                    invalid.UnionWith(visited);
                    break;
                }

                if (current.ParentMeterId is not { } parentId ||
                    !byId.TryGetValue(parentId, out ParkEnergyMeterInput? parent))
                {
                    break;
                }

                current = parent;
            }
        }

        return invalid;
    }

    /// <summary>
    /// 把一段区间内的用量按时长比例摊到小时桶。
    /// </summary>
    private static void Distribute(
        decimal value,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        TimeSpan offset,
        double[] buckets,
        bool[] touched)
    {
        DateTimeOffset segmentStart = from > dayStart ? from : dayStart;
        DateTimeOffset segmentEnd = to < dayEnd ? to : dayEnd;
        double totalSeconds = (to - from).TotalSeconds;

        if (totalSeconds <= 0 || segmentEnd <= segmentStart)
        {
            return;
        }

        double remaining = (double)value * ((segmentEnd - segmentStart).TotalSeconds / totalSeconds);

        while (segmentStart < segmentEnd)
        {
            int hourIndex = (int)(segmentStart.ToOffset(offset) - dayStart).TotalHours;
            if (hourIndex is < 0 or > 23)
            {
                break;
            }

            DateTimeOffset hourEnd = dayStart.AddHours(hourIndex + 1);
            DateTimeOffset sliceEnd = segmentEnd < hourEnd ? segmentEnd : hourEnd;

            // 每片按「原始总量 × 本片时长占比」计算。用递减的 remaining 再乘比例会
            // 得到几何衰减的分配（4 小时段变成 4000/3000/2250/6750），曲线锯齿状；
            // 最后一片取余量，保证合计与总量严格相等。
            double portion = sliceEnd >= segmentEnd
                ? remaining
                : (double)value * ((sliceEnd - segmentStart).TotalSeconds / totalSeconds);

            buckets[hourIndex] += portion;
            touched[hourIndex] = true;
            remaining -= portion;
            segmentStart = sliceEnd;
        }
    }

    private sealed class MediumAccumulator(string code, string name, string unit, bool isPrimary)
    {
        private decimal _quantity;
        private decimal? _coal;
        private int _meters;

        public void Add(decimal quantity, decimal? coal)
        {
            _quantity += quantity;
            _meters++;
            if (coal.HasValue)
            {
                _coal = (_coal ?? 0m) + coal.Value;
            }
        }

        public ParkEnergyMediumUsage ToUsage()
        {
            return new ParkEnergyMediumUsage(code, name, unit, Math.Round(_quantity, 4),
                _coal is null ? null : Math.Round(_coal.Value, 4),
                isPrimary, _meters);
        }
    }

    private sealed class OrgAccumulator(Guid id, string name)
    {
        private decimal _quantity;
        private decimal? _coal;

        public void Add(decimal quantity, decimal? coal)
        {
            _quantity += quantity;
            if (coal.HasValue)
            {
                _coal = (_coal ?? 0m) + coal.Value;
            }
        }

        public ParkEnergyOrgUsage ToUsage()
        {
            return new ParkEnergyOrgUsage(id, name, _coal is null ? null : Math.Round(_coal.Value, 4),
                Math.Round(_quantity, 2));
        }
    }
}
