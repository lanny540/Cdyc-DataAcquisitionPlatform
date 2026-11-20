using DAP.Core.Domain.Entities;
using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 开发环境的园区能源种子数据。
///
/// **只在「库里一块表都没有」时执行一次。** 一旦有了计量点就整体跳过，
/// 因此后续无论怎么改台账、补读数、清系数，重启都不会被这层数据盖回去。
/// 想重新灌一份，把 <c>energy_readings</c> 与 <c>energy_meters</c> 清空后重启即可。
///
/// 与 <see cref="ParkEnergySampleProvider"/> 的区别要分清：示例数据只存在于内存，
/// 页面标 dataStatus=sample，用来预览版式；本类是真正落库的记录，
/// 走的是与正式环境完全相同的聚合链路（差分、倍率、折标、组织汇总），
/// 页面会按 live 渲染，台账页也能直接编辑与删除——这才有测试价值。
///
/// 数字之间刻意保持可验算：五块电表日均 10.3 万 kWh，月度约 287 万 kWh，
/// 折标约 353 tce；两块蒸汽表月耗约 1,060 t，按 92 kgce/t 折标约 98 tce；
/// 水 3 万 t 折标约 7.7 tce；三项合计约 458 tce，与页面上的综合能耗对得上。
/// </summary>
public sealed class ParkEnergyDevDataInitializer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<ParkEnergyDevDataInitializer> logger) : IHostedService
{
    /// <summary>
    /// 回填的历史天数，含今天。
    ///
    /// 取两个整年而不是一个月，是为了让三种视图同时自洽：
    /// 月度要看「上月同期」（最早可能是 61 天前，当月 31 天 + 上月 31 天）；
    /// 年度要看全年累计，只回填两个月会让年度总量凭空少掉六分之五；
    /// 年度同比还要取到去年同期间，少于两年就永远是「上期无数据」。
    /// 代价是首次启动多写几千行，对本地库只是一两秒的事。
    /// </summary>
    private const int SeedDays = 730;

    /// <summary>
    /// 最近若干天按小时抄见。园区电表本来就是小时级采集，
    /// 日抄会让负荷曲线退化成 24 段等分，看不出班次。
    /// </summary>
    private const int HourlyDays = 3;

    /// <summary>
    /// 蒸汽折标煤系数的开发占位值，单位 kgce/t。
    ///
    /// 92 对应约 2.7 GJ/t 的蒸汽焓值（1 kgce = 29.3076 MJ）。真实值取决于供汽焓值与
    /// 锅炉效率，正式环境必须在台账页按实际参数改写——只在首次种子时写入，
    /// 用户清空后重启不会被重新填上。
    /// </summary>
    private const decimal SteamPlaceholderFactor = 92m;

    /// <summary>
    /// 24 小时的用能权重，反映三班制园区的作息：夜间低谷、上午与下午双峰。
    ///
    /// 只用于在同一块表的日用量内部做分配，权重之和会被归一，因此不影响日总量，
    /// 月度汇总仍然严格等于各日用量之和。
    /// </summary>
    private static readonly decimal[] HourWeights =
    [
        0.62m, 0.58m, 0.55m, 0.55m, 0.60m, 0.70m, 0.86m, 1.00m,
        1.10m, 1.16m, 1.18m, 1.12m, 0.98m, 1.08m, 1.14m, 1.10m,
        1.02m, 0.90m, 0.80m, 0.74m, 0.70m, 0.66m, 0.64m, 0.62m
    ];

    private static readonly decimal HourWeightSum = HourWeights.Sum();

    /// <summary>
    /// 组织树。园区根节点由字典初始化器负责，这里只补部门与工序。
    ///
    /// 工序节点建出来但不挂表：先把层级立住，等现场真的分出工段计量时，
    /// 新增计量点直接选节点即可，不用再改一次树。
    /// </summary>
    private static readonly (string Code, string Name, string Level, string ParentCode, int SortOrder)[] OrgPlan =
    [
        ("DEPT-PAPER", "钞纸车间", EnergyOrgUnit.DepartmentLevel, "PARK", 10),
        ("DEPT-PRINT", "印钞车间", EnergyOrgUnit.DepartmentLevel, "PARK", 20),
        ("DEPT-POWER", "动力车间", EnergyOrgUnit.DepartmentLevel, "PARK", 30),
        ("DEPT-ADMIN", "行政后勤", EnergyOrgUnit.DepartmentLevel, "PARK", 40),
        ("DEPT-PUBLIC", "公共设施", EnergyOrgUnit.DepartmentLevel, "PARK", 50),
        ("DEPT-SECURITY", "保卫部", EnergyOrgUnit.DepartmentLevel, "PARK", 60),
        ("PROC-PAPER-PREP", "备料工段", EnergyOrgUnit.ProcessLevel, "DEPT-PAPER", 10),
        ("PROC-PAPER-MAKE", "造纸工段", EnergyOrgUnit.ProcessLevel, "DEPT-PAPER", 20),
        ("PROC-PAPER-FINISH", "加工工段", EnergyOrgUnit.ProcessLevel, "DEPT-PAPER", 30),
        ("PROC-PRINT-PRESS", "印刷工段", EnergyOrgUnit.ProcessLevel, "DEPT-PRINT", 10),
        ("PROC-PRINT-CHECK", "检封工段", EnergyOrgUnit.ProcessLevel, "DEPT-PRINT", 20),
        ("PROC-POWER-STEAM", "蒸汽站", EnergyOrgUnit.ProcessLevel, "DEPT-POWER", 10),
        ("PROC-POWER-AIR", "空压站", EnergyOrgUnit.ProcessLevel, "DEPT-POWER", 20)
    ];

    /// <summary>
    /// 计量点。四维标签覆盖全部 5 类用途与 5 类设备系统，
    /// 页面按用途看构成、按组织看排名时都不会有空列。
    /// </summary>
    private static readonly MeterPlan[] MeterPlans =
    [
        new("ELEC-PAPER", "钞纸车间总电表", "ELEC", "DEPT-PAPER", "PROD", "PROCESS", true, 42_000m, 1_286_400m, 0,
            "钞纸生产线主进线，小时级抄见") { Hourly = true },
        new("ELEC-PRINT", "印钞车间总电表", "ELEC", "DEPT-PRINT", "PROD", "PROCESS", true, 33_000m, 986_200m, 2,
            "印钞生产线主进线，小时级抄见") { Hourly = true },
        new("ELEC-POWER", "动力车间总电表", "ELEC", "DEPT-POWER", "AUX", "POWER", true, 18_000m, 542_800m, 4,
            "锅炉房与空压站动力用电") { Hourly = true },
        new("ELEC-ADMIN", "行政后勤总电表", "ELEC", "DEPT-ADMIN", "OFFICE", "HVAC", true, 4_200m, 128_600m, 1,
            "办公楼空调用电为主") { Hourly = true },
        new("ELEC-PUBLIC", "公共设施总电表", "ELEC", "DEPT-PUBLIC", "PUBLIC", "LIGHTING", true, 5_800m, 173_900m, 3,
            "园区道路与景观照明") { Hourly = true },
        new("ELEC-SECURITY", "安防监控电表", "ELEC", "DEPT-SECURITY", "SECURITY", "IT", true, 510m, 21_400m, 0,
            "门禁、监控与机房弱电") { Hourly = true },
        new("STEAM-PAPER", "钞纸车间蒸汽表", "STEAM", "DEPT-PAPER", "PROD", "PROCESS", true, 24m, 8_240m, 0,
            "烘干工艺用汽"),
        new("STEAM-PRINT", "印钞车间蒸汽表", "STEAM", "DEPT-PRINT", "PROD", "PROCESS", true, 14m, 5_180m, 0,
            "印刷工艺用汽"),
        new("WATER-PARK", "园区总水表", "WATER", "DEPT-POWER", "AUX", "POWER", true, 1_050m, 96_500m, 0,
            "供水站房总表，暂未按部门分摊"),
        new("AIR-POWER", "空压站供气表", "AIR", "DEPT-POWER", "AUX", "POWER", false, 21_000m, 0m, 0,
            "积算仪按日区间用量上传，非累计示数"),
        new("COOLING-PUBLIC", "办公楼冷量表", "COOLING", "DEPT-PUBLIC", "PUBLIC", "HVAC", true, 260m, 42_600m, 2,
            "中央空调冷量，二次能源只作分项")
    ];

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!ShouldSeed())
        {
            return;
        }

        try
        {
            await SeedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // 与字典初始化器一致：附属模块灌数据失败不该把整个站点拖住。
            logger.LogError(ex, "园区能源开发种子数据写入失败，能源页面将按空库状态展示。");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 是否执行种子。Development 默认执行，可用 Energy:SeedDevData=false 关闭；
    /// 其他环境默认不执行，避免测试数据混进正式库。
    /// </summary>
    private bool ShouldSeed()
    {
        return configuration.GetValue<bool?>("Energy:SeedDevData") ?? environment.IsDevelopment();
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        DataAcquisitionPlatformDbContext db =
            scope.ServiceProvider.GetRequiredService<DataAcquisitionPlatformDbContext>();

        if (await db.EnergyMeters.AnyAsync(cancellationToken))
        {
            logger.LogInformation("园区能源已存在计量点，跳过开发种子数据。");
            return;
        }

        EnergyOrgUnit? park = await db.EnergyOrgUnits
            .FirstOrDefaultAsync(unit => unit.Level == EnergyOrgUnit.ParkLevel, cancellationToken);

        if (park is null)
        {
            logger.LogWarning("未找到园区根节点，跳过园区能源开发种子数据。");
            return;
        }

        Dictionary<string, Guid> media = await db.EnergyMedia.AsNoTracking()
            .ToDictionaryAsync(item => item.Code, item => item.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
        Dictionary<string, Guid> usages = await db.EnergyUsageTypes.AsNoTracking()
            .ToDictionaryAsync(item => item.Code, item => item.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
        Dictionary<string, Guid> systems = await db.EnergySystems.AsNoTracking()
            .ToDictionaryAsync(item => item.Code, item => item.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        await EnsureSteamFactorAsync(db, now, cancellationToken);

        Dictionary<string, Guid> orgs = await EnsureOrgUnitsAsync(db, park, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        TimeSpan offset = TimeSpan.FromHours(configuration.GetValue("Energy:UtcOffsetHours", 8d));
        DateTimeOffset localNow = DateTimeOffset.UtcNow.ToOffset(offset);
        DateOnly today = DateOnly.FromDateTime(localNow.DateTime);
        int lastHour = localNow.Hour;

        List<(EnergyMeter Meter, MeterPlan Plan)> meters = new();
        List<string> skipped = new();

        foreach (MeterPlan plan in MeterPlans)
        {
            if (!media.TryGetValue(plan.MediumCode, out Guid mediumId) ||
                !usages.TryGetValue(plan.UsageCode, out Guid usageId) ||
                !systems.TryGetValue(plan.SystemCode, out Guid systemId) ||
                !orgs.TryGetValue(plan.OrgCode, out Guid orgId))
            {
                // 字典被停用或改编码时不硬凑：少一块表比挂错维度好排查。
                skipped.Add(plan.Code);
                continue;
            }

            EnergyMeter meter = new()
            {
                Id = Guid.NewGuid(),
                Code = plan.Code,
                Name = plan.Name,
                MediumId = mediumId,
                OrgUnitId = orgId,
                UsageTypeId = usageId,
                SystemTypeId = systemId,
                ParentMeterId = null,
                // 种子只建部门/介质口径的表，不建园区总表，避免与分表重复求和。
                IsSummary = false,
                Ratio = 1m,
                IsCumulative = plan.IsCumulative,
                IsEnabled = true,
                Remark = plan.Remark,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.EnergyMeters.Add(meter);
            meters.Add((meter, plan));
        }

        if (meters.Count == 0)
        {
            logger.LogWarning("能源字典不完整，未能建立任何计量点。缺失：{Codes}", string.Join("、", skipped));
            return;
        }

        await db.SaveChangesAsync(cancellationToken);

        int readingCount = 0;
        foreach ((EnergyMeter meter, MeterPlan plan) in meters)
        {
            List<EnergyReading> readings = BuildReadings(meter.Id, plan, today, lastHour, offset, now);
            db.EnergyReadings.AddRange(readings);
            readingCount += readings.Count;
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "园区能源开发种子数据已写入：{Meters} 块计量点、{Readings} 条读数，覆盖 {From:yyyy-MM-dd} 至 {To:yyyy-MM-dd HH:mm}。",
            meters.Count,
            readingCount,
            today.AddDays(-(SeedDays - 1)),
            localNow);

        if (skipped.Count > 0)
        {
            logger.LogWarning("以下计量点因字典缺失未建立：{Codes}", string.Join("、", skipped));
        }
    }

    /// <summary>
    /// 首次种子时给蒸汽补一个占位折标系数。
    ///
    /// 字典默认留空是对的——蒸汽系数取决于焓值。但开发库全是空值的话，
    /// 综合能耗会长期缺掉近四分之一，页面只剩一条警告可看，反而测不出别的问题。
    /// 因此这里只在「还没配过」时写一次，且不覆盖用户后来改成的任何值。
    /// </summary>
    private static async Task EnsureSteamFactorAsync(
        DataAcquisitionPlatformDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        EnergyMedium? steam = await db.EnergyMedia
            .FirstOrDefaultAsync(item => item.Code == "STEAM", cancellationToken);

        if (steam is null || steam.CoalFactor is not null)
        {
            return;
        }

        steam.CoalFactor = SteamPlaceholderFactor;
        steam.UpdatedAt = now;
    }

    private static async Task<Dictionary<string, Guid>> EnsureOrgUnitsAsync(
        DataAcquisitionPlatformDbContext db,
        EnergyOrgUnit park,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await db.EnergyOrgUnits.AsNoTracking()
            .Select(unit => new { unit.Code, unit.Id })
            .ToListAsync(cancellationToken);

        Dictionary<string, Guid> map = new(StringComparer.Ordinal);
        foreach (var item in existing)
        {
            map[item.Code] = item.Id;
        }

        // 园区根节点的编码可能被现场改过，按层级兜底登记，避免子节点挂不上。
        map[park.Code] = park.Id;

        foreach ((string Code, string Name, string Level, string ParentCode, int SortOrder) seed in OrgPlan)
        {
            if (map.ContainsKey(seed.Code))
            {
                continue;
            }

            if (!map.TryGetValue(seed.ParentCode, out Guid parentId))
            {
                continue;
            }

            Guid id = Guid.NewGuid();
            db.EnergyOrgUnits.Add(new EnergyOrgUnit
            {
                Id = id,
                Code = seed.Code,
                Name = seed.Name,
                Level = seed.Level,
                ParentId = parentId,
                SortOrder = seed.SortOrder,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            });

            map[seed.Code] = id;
        }

        return map;
    }

    /// <summary>
    /// 按「累计示数 + 日用量」生成读数序列。
    ///
    /// 关键是示数必须单调递增：先落一条起点示数，再逐段加上该段用量。
    /// 若按「每天独立随机一个示数」，月度差分会出现负值，聚合器会把整块表按 0 计，
    /// 页面看起来只是「这个月用得特别少」，很难联想到是种子数据造错了。
    /// </summary>
    private static List<EnergyReading> BuildReadings(
        Guid meterId,
        MeterPlan plan,
        DateOnly today,
        int lastHour,
        TimeSpan offset,
        DateTimeOffset createdAt)
    {
        List<EnergyReading> readings = new();
        DateOnly firstDay = today.AddDays(-(SeedDays - 1));
        DateOnly hourlyFrom = today.AddDays(-(HourlyDays - 1));

        // 电表推进到今天当前整点：这样负荷曲线有当天的数据，而不是永远停在昨天。
        // 其余介质按日抄见，最后一条停在今天零点——当天还没抄表，本来就不该有数。
        DateTimeOffset last = plan.Hourly
            ? new DateTimeOffset(today.ToDateTime(new TimeOnly(lastHour, 0)), offset)
            : new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), offset);

        DateTimeOffset cursor = new(firstDay.ToDateTime(TimeOnly.MinValue), offset);
        decimal value = plan.StartReading;

        while (cursor < last)
        {
            DateTimeOffset local = cursor.ToOffset(offset);
            DateOnly date = DateOnly.FromDateTime(local.DateTime);
            bool hourly = plan.Hourly && date >= hourlyFrom;

            DateTimeOffset next = cursor.Add(hourly ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1));
            if (next > last)
            {
                next = last;
            }

            decimal dayTotal = plan.DailyUsage * DayFactor(StableSeed(plan.Code), date, firstDay.Year);
            decimal quantity = hourly
                ? dayTotal * HourWeights[(local.Hour + plan.Phase) % 24] / HourWeightSum
                : dayTotal * (decimal)(next - cursor).TotalDays;

            if (plan.IsCumulative)
            {
                value += quantity;

                // 累计示数记在区间结束时刻：示数的含义就是「截至此刻的累计值」。
                readings.Add(new EnergyReading
                {
                    Id = Guid.NewGuid(),
                    MeterId = meterId,
                    ReadAt = next.ToUniversalTime(),
                    Value = Math.Round(value, 3),
                    Source = EnergyReading.CollectionSource,
                    CreatedAt = createdAt
                });
            }
            else
            {
                // 非累计表约定读数即本区间用量，记在区间起点，月度归属才不会整体后移一天。
                readings.Add(new EnergyReading
                {
                    Id = Guid.NewGuid(),
                    MeterId = meterId,
                    ReadAt = cursor.ToUniversalTime(),
                    Value = Math.Round(quantity, 3),
                    Source = EnergyReading.CollectionSource,
                    CreatedAt = createdAt
                });
            }

            cursor = next;
        }

        return readings;
    }

    /// <summary>
    /// 日用量波动：工作日按基准，周末降至 86%，再叠一层 ±8% 的确定性抖动。
    ///
    /// 刻意不用 <see cref="Random"/>：同一天重新灌库要得到同样的数，
    /// 否则截图与说明对不上时，没人分得清是代码改了还是随机数变了。
    ///
    /// 年度因子（每年 +1.8%）只为一件事：种子覆盖两个年度，若两年的分布完全一致，
    /// 年度同比会稳定停在 0%，那一列看起来像没接上数据。它不代表真实产能变化。
    /// </summary>
    private static decimal DayFactor(int seed, DateOnly date, int baseYear)
    {
        decimal weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0.86m : 1m;
        decimal trend = 1m + ((date.Year - baseYear) * 0.018m);
        return weekend * trend * (0.92m + (Jitter(seed, date.DayNumber) * 0.16m));
    }

    /// <summary>
    /// 确定性伪随机：FNV-1a 变体，返回 [0, 1)。
    /// </summary>
    private static decimal Jitter(int seed, int salt)
    {
        unchecked
        {
            uint hash = ((uint)seed * 16_777_619u) ^ ((uint)salt * 2_654_435_761u);
            hash ^= hash >> 13;
            hash *= 16_777_619u;
            hash ^= hash >> 17;
            return hash % 1000u / 1000m;
        }
    }

    /// <summary>
    /// 由计量点编码派生抖动种子，让各块表的日波动彼此错开。
    /// 不用 <c>string.GetHashCode()</c>：.NET 的字符串哈希逐进程随机化，
    /// 同一天重灌两次得到的曲线会不一样。
    /// </summary>
    private static int StableSeed(string code)
    {
        unchecked
        {
            uint hash = 2_166_136_261u;
            foreach (char ch in code)
            {
                hash ^= ch;
                hash *= 16_777_619u;
            }

            return (int)(hash & 0x7FFF_FFFF);
        }
    }

    /// <summary>
    /// 一块种子计量点的定义。
    /// </summary>
    /// <param name="Code">计量点编码，同时作为外部推送读数时的业务主键。</param>
    /// <param name="Name">名称。</param>
    /// <param name="MediumCode">介质编码。</param>
    /// <param name="OrgCode">组织节点编码。</param>
    /// <param name="UsageCode">用途编码。</param>
    /// <param name="SystemCode">设备系统编码。</param>
    /// <param name="IsCumulative">读数是否为累计示数。</param>
    /// <param name="DailyUsage">日均用量，单位同介质。</param>
    /// <param name="StartReading">累计表示数的起始值，仅累计表使用。</param>
    /// <param name="Phase">小时权重相位，用于让各表曲线错开。</param>
    /// <param name="Remark">备注。</param>
    private sealed record MeterPlan(
        string Code,
        string Name,
        string MediumCode,
        string OrgCode,
        string UsageCode,
        string SystemCode,
        bool IsCumulative,
        decimal DailyUsage,
        decimal StartReading,
        int Phase,
        string Remark)
    {
        /// <summary>
        /// 获取或设置是否按小时抄见。只有电表按小时推进。
        /// </summary>
        public bool Hourly { get; init; }
    }
}
