using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Services;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 园区能源查询 / 台账服务的数据库级单元测试。
///
/// 服务内部聚合口径委托给 <c>ParkEnergyAggregator</c>（已有专门测试），
/// 这里钉住的是「服务层」的行为：期间归一、示例数据开关、空态提示、
/// 读数装载窗口、折标配置联动，以及台账增删改的全部校验分支。
/// 唯一不覆盖 <c>IngestReadingsAsync</c> 的写入路径：内部强转
/// NpgsqlConnection 执行 NpgsqlBatch，SQLite 下必然失败（读数一律直接播种实体）。
///
/// 期间计算依赖服务内部的 DateTime.Now（不可注入），因此读数时刻
/// 全部以「当前月份」为基准动态构造；园区时区固定 +8，与运行机器一致。
/// </summary>
public sealed class ParkEnergyQueryServiceTests
{
    /// <summary>园区时区，与服务默认值一致。</summary>
    private static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    /// <summary>
    /// 播种能源字典并返回常用维度标识（介质 / 用途 / 系统 / 园区根节点）。
    /// </summary>
    private static async Task<CatalogRefs> SeedCatalogAsync(EnergySqliteTestDatabase database)
    {
        await new EnergyDictionaryInitializer(
                new EnergyScopeFactory(database),
                NullLogger<EnergyDictionaryInitializer>.Instance)
            .StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        EnergyMedium elec = await db.EnergyMedia.SingleAsync(item => item.Code == "ELEC");
        EnergyMedium water = await db.EnergyMedia.SingleAsync(item => item.Code == "WATER");
        EnergyMedium steam = await db.EnergyMedia.SingleAsync(item => item.Code == "STEAM");
        EnergyMedium cooling = await db.EnergyMedia.SingleAsync(item => item.Code == "COOLING");
        EnergyUsageType usage = await db.EnergyUsageTypes.SingleAsync(item => item.Code == "PROD");
        EnergySystemType system = await db.EnergySystems.SingleAsync(item => item.Code == "PROCESS");
        EnergyOrgUnit park = await db.EnergyOrgUnits.SingleAsync(item => item.Level == EnergyOrgUnit.ParkLevel);

        return new CatalogRefs(elec.Id, water.Id, steam.Id, cooling.Id, usage.Id, system.Id, park.Id);
    }

    /// <summary>新增一块计量点并返回标识。非空字段全部显式赋值（SQLite 已剥离 PG 默认值）。</summary>
    private static async Task<Guid> AddMeterAsync(
        EnergySqliteTestDatabase database,
        CatalogRefs refs,
        string code,
        Guid? mediumId = null,
        Guid? orgUnitId = null,
        bool isCumulative = true,
        decimal ratio = 1m,
        Guid? parentMeterId = null)
    {
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        EnergyMeter meter = new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"{code} 名称",
            MediumId = mediumId ?? refs.ElecMediumId,
            OrgUnitId = orgUnitId ?? refs.ParkOrgId,
            UsageTypeId = refs.ProdUsageId,
            SystemTypeId = refs.ProcessSystemId,
            ParentMeterId = parentMeterId,
            IsCumulative = isCumulative,
            Ratio = ratio,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.EnergyMeters.Add(meter);
        await db.SaveChangesAsync();
        return meter.Id;
    }

    /// <summary>直接播种一条读数（绕过 Npgsql 专用的写入接口）。</summary>
    private static async Task AddReadingAsync(EnergySqliteTestDatabase database, Guid meterId, DateTimeOffset readAt, decimal value)
    {
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        db.EnergyReadings.Add(new EnergyReading
        {
            Id = Guid.NewGuid(),
            MeterId = meterId,
            ReadAt = readAt,
            Value = value,
            Source = EnergyReading.ManualSource,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static ParkEnergyQueryService CreateService(
        EnergySqliteTestDatabase database,
        IHostEnvironment? environment = null,
        params (string Key, string? Value)[] config)
    {
        return new ParkEnergyQueryService(
            database.CreateContext(),
            HostingFakes.CreateConfiguration(config),
            environment ?? HostingFakes.CreateDevelopmentEnvironment(),
            NullLogger<ParkEnergyQueryService>.Instance);
    }

    /// <summary>当月起始日（本地日期，与服务内部口径一致）。</summary>
    private static DateOnly MonthStart()
    {
        return new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 1);
    }

    private static DateTimeOffset At(DateOnly day, int hour)
    {
        return new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, 0)), Offset);
    }

    // ---------------------------------------------------------------
    // GetOverviewAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetOverview_WithoutMeters_Should_Return_Empty_With_TaiZhang_Hint()
    {
        using EnergySqliteTestDatabase database = new();
        ParkEnergyQueryService service = CreateService(database);

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month");

        // 「没有计量点」要去建台账，与「有表没读数」的提示必须区分。
        Assert.Equal(ParkEnergyDataStatus.Empty, overview.DataStatus);
        Assert.Empty(overview.Kpis);
        Assert.Empty(overview.LoadPoints);
        Assert.Contains(overview.Warnings, warning => warning.Contains("尚未配置能源计量点"));
    }

    [Fact]
    public async Task GetOverview_MetersWithoutReadings_Should_Return_Empty_With_Push_Hint()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        await AddMeterAsync(database, refs, "EM-A-001");

        ParkEnergyQueryService service = CreateService(database);

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month");

        Assert.Equal(ParkEnergyDataStatus.Empty, overview.DataStatus);
        Assert.Contains(overview.Warnings, warning => warning.Contains("统计期间内没有能源读数"));
        Assert.Contains(overview.Warnings, warning => warning.Contains("能源平台"));
    }

    [Fact]
    public async Task GetOverview_UnknownPeriod_Should_Fall_Back_To_Month()
    {
        using EnergySqliteTestDatabase database = new();
        ParkEnergyQueryService service = CreateService(database);

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("not-a-period");

        Assert.Equal("month", overview.Period);
        Assert.Equal(MonthStart(), overview.PeriodStart);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), overview.PeriodEnd);
        Assert.Contains($"{MonthStart().Year} 年 {MonthStart().Month} 月", overview.PeriodLabel);
    }

    [Fact]
    public async Task GetOverview_YearPeriod_CaseInsensitive_Should_Start_At_January()
    {
        using EnergySqliteTestDatabase database = new();
        ParkEnergyQueryService service = CreateService(database);

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("YEAR");

        Assert.Equal("year", overview.Period);
        Assert.Equal(new DateOnly(DateTime.Now.Year, 1, 1), overview.PeriodStart);
        Assert.Equal($"{DateTime.Now.Year} 年", overview.PeriodLabel);
    }

    [Fact]
    public async Task GetOverview_DemoInProduction_Should_Be_Refused()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        // 有表也拦：demo 判定在读表之前，生产环境拼 ?demo=true 拿不到示例数据。
        await AddMeterAsync(database, refs, "EM-A-001");

        ParkEnergyQueryService service = CreateService(
            database, HostingFakes.CreateProductionEnvironment());

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month", demo: true);

        Assert.Equal(ParkEnergyDataStatus.Empty, overview.DataStatus);
        Assert.Contains(overview.Warnings, warning => warning.Contains("当前环境未开启示例数据预览"));
    }

    [Fact]
    public async Task GetOverview_DemoInDevelopment_Should_Return_Sample()
    {
        using EnergySqliteTestDatabase database = new();
        ParkEnergyQueryService service = CreateService(database, HostingFakes.CreateDevelopmentEnvironment());

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month", demo: true);

        Assert.Equal(ParkEnergyDataStatus.Sample, overview.DataStatus);
        Assert.Equal(24, overview.LoadPoints.Count);
        Assert.Equal(4, overview.Kpis.Count);
        Assert.Contains(overview.Kpis, kpi => kpi.Key == "totalTce");
    }

    [Fact]
    public async Task GetOverview_DemoInProduction_WithAllowSamplePreview_Should_Return_Sample()
    {
        using EnergySqliteTestDatabase database = new();
        ParkEnergyQueryService service = CreateService(
            database,
            HostingFakes.CreateProductionEnvironment(),
            ("Energy:AllowSamplePreview", "true"));

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("year", demo: true);

        // 配置开关是显式的产品决定（例如预发环境），允许在非开发环境放行。
        Assert.Equal(ParkEnergyDataStatus.Sample, overview.DataStatus);
        Assert.Equal("year", overview.Period);
        Assert.Contains(overview.Kpis, kpi => kpi.Delta != null && kpi.Delta.Contains("同比"));
    }

    [Fact]
    public async Task GetOverview_CumulativeMeter_Should_Diff_With_CoalConversion_And_Delta()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        Guid meterId = await AddMeterAsync(database, refs, "EM-DIFF-001");

        DateOnly monthStart = MonthStart();
        DateOnly prevStart = monthStart.AddMonths(-1);

        // 上一期间：9 月 28 日基线 100，上月 1 日两条读数 → 用量 1000 kWh。
        await AddReadingAsync(database, meterId, At(prevStart.AddDays(-3), 12), 100m);
        await AddReadingAsync(database, meterId, At(prevStart, 1), 1050m);
        await AddReadingAsync(database, meterId, At(prevStart, 2), 1100m);

        // 本期间：期间前基线 1100，当月 1 日两条读数 → 用量 2000 kWh。
        await AddReadingAsync(database, meterId, At(monthStart, 1), 2100m);
        await AddReadingAsync(database, meterId, At(monthStart, 2), 3100m);

        ParkEnergyQueryService service = CreateService(database);

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month");

        Assert.Equal(ParkEnergyDataStatus.Live, overview.DataStatus);
        Assert.Empty(overview.Warnings);
        Assert.Equal(At(monthStart, 2), overview.DataThrough);

        // 综合能耗 = 2000 kWh × 0.1229 / 1000 = 0.2458 tce → 展示文本 0.25。
        ParkEnergyKpiDto total = Assert.Single(overview.Kpis, kpi => kpi.Key == "totalTce");
        Assert.Equal("0.25", total.Value);
        Assert.Equal("↑ 100% 环比", total.Delta);
        Assert.False(total.IsImprovement);

        ParkEnergyKpiDto elec = Assert.Single(overview.Kpis, kpi => kpi.Key == "elec");
        Assert.Equal("总用电量", elec.Label);
        Assert.Equal("2,000", elec.Value);
        Assert.Equal("kWh", elec.Unit);
        Assert.Equal("↑ 100% 环比", elec.Delta);
        Assert.False(elec.IsImprovement);
        Assert.Contains("折标", elec.Hint);

        // 一次能源构成：只有电，占比 100%。
        ParkEnergyShareDto share = Assert.Single(overview.PrimaryShares);
        Assert.Equal("电", share.Name);
        Assert.Equal(1m, share.Share);
        Assert.Equal("100%", share.ShareText);

        // 组织行：折标量与环比变化。
        ParkEnergyOrgShareDto org = Assert.Single(overview.OrgRows);
        Assert.Equal(1m, org.Share);
        Assert.Equal(100m, org.DeltaPercent);
    }

    [Fact]
    public async Task GetOverview_CumulativeMeter_Should_Produce_Hourly_Load()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        Guid meterId = await AddMeterAsync(database, refs, "EM-LOAD-001");

        DateOnly monthStart = MonthStart();
        await AddReadingAsync(database, meterId, At(monthStart, 1), 2100m);
        await AddReadingAsync(database, meterId, At(monthStart, 2), 3100m);

        ParkEnergyQueryService service = CreateService(database);

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month");

        // 01:00→02:00 走了 1000 kWh，平均功率 1000 kW，落在负荷曲线第 1 小时。
        ParkEnergyLoadPointDto point = Assert.Single(overview.LoadPoints);
        Assert.Equal(1, point.Hour);
        Assert.Equal(1000m, point.LoadKw);
    }

    [Fact]
    public async Task GetOverview_SingleReadingWithoutBaseline_Should_Report_Unknown_Not_Zero()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        Guid meterId = await AddMeterAsync(database, refs, "EM-SHORT-001");

        // 期间内只有一条累计读数且无期间前基线：用量是「无法计算」，不能显示成 0。
        await AddReadingAsync(database, meterId, At(MonthStart(), 12), 1200m);

        ParkEnergyQueryService service = CreateService(database);

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month");

        Assert.Equal(ParkEnergyDataStatus.Live, overview.DataStatus);
        Assert.Contains(overview.Warnings, warning => warning.Contains("读数不足"));
        ParkEnergyKpiDto total = Assert.Single(overview.Kpis);
        Assert.Equal("totalTce", total.Key);
        Assert.Equal("0", total.Value);
        Assert.Null(total.Delta);
        Assert.Null(total.IsImprovement);
    }

    [Fact]
    public async Task GetOverview_Ratio_Should_Be_Applied_To_Difference()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        // 倍率 40：示数差 10 → 实际用量 400 kWh。少乘倍率会小 40 倍。
        Guid meterId = await AddMeterAsync(database, refs, "EM-RATIO-001", ratio: 40m);

        DateOnly monthStart = MonthStart();
        await AddReadingAsync(database, meterId, At(monthStart.AddDays(-2), 12), 100m);
        await AddReadingAsync(database, meterId, At(monthStart, 6), 110m);

        ParkEnergyQueryService service = CreateService(database);

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month");

        ParkEnergyKpiDto elec = Assert.Single(overview.Kpis, kpi => kpi.Key == "elec");
        Assert.Equal("400", elec.Value);
    }

    [Fact]
    public async Task GetOverview_NonCumulativeMeter_Should_Sum_Interval_Usage()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        // 流量计约定读数即区间用量，直接相加再乘倍率：(12 + 8) × 2 = 40 t。
        Guid meterId = await AddMeterAsync(
            database, refs, "EM-WATER-001", mediumId: refs.WaterMediumId, isCumulative: false, ratio: 2m);

        DateOnly monthStart = MonthStart();
        await AddReadingAsync(database, meterId, At(monthStart, 1), 12m);
        await AddReadingAsync(database, meterId, At(monthStart, 2), 8m);

        ParkEnergyQueryService service = CreateService(database);

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month");

        ParkEnergyKpiDto water = Assert.Single(overview.Kpis, kpi => kpi.Key == "water");
        Assert.Equal("用水量", water.Label);
        Assert.Equal("40", water.Value);
        Assert.Equal("t", water.Unit);
    }

    [Fact]
    public async Task GetOverview_MeterReset_Should_Count_As_Zero_With_Warning()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        Guid meterId = await AddMeterAsync(database, refs, "EM-RESET-001");

        DateOnly monthStart = MonthStart();
        await AddReadingAsync(database, meterId, At(monthStart.AddDays(-2), 12), 5000m);
        await AddReadingAsync(database, meterId, At(monthStart, 1), 5000m);
        await AddReadingAsync(database, meterId, At(monthStart, 2), 20m);

        ParkEnergyQueryService service = CreateService(database);

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month");

        // 换表 / 清零按 0 计并提示，不能取绝对值造出巨额能耗。
        Assert.Contains(overview.Warnings, warning => warning.Contains("示数回退"));
        ParkEnergyKpiDto elec = Assert.Single(overview.Kpis, kpi => kpi.Key == "elec");
        Assert.Equal("0", elec.Value);
    }

    [Fact]
    public async Task GetOverview_DeclaredDemand_Should_Come_From_Config()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        Guid meterId = await AddMeterAsync(database, refs, "EM-DEMAND-001");

        DateOnly monthStart = MonthStart();
        await AddReadingAsync(database, meterId, At(monthStart.AddDays(-1), 12), 0m);
        await AddReadingAsync(database, meterId, At(monthStart, 1), 1000m);

        ParkEnergyQueryService service = CreateService(
            database, config: ("Energy:DeclaredDemandKw", "4500"));

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month");

        Assert.Equal(4500m, overview.DeclaredDemandKw);
    }

    [Fact]
    public async Task GetOverview_MissingCoalFactor_Should_Warn_And_Exclude_From_Total()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        // 蒸汽字典默认没有折标系数：实物量照常出现，但不进综合能耗，且要有提示。
        Guid meterId = await AddMeterAsync(database, refs, "EM-STEAM-001", mediumId: refs.SteamMediumId);

        DateOnly monthStart = MonthStart();
        await AddReadingAsync(database, meterId, At(monthStart.AddDays(-1), 12), 0m);
        await AddReadingAsync(database, meterId, At(monthStart, 1), 120m);

        ParkEnergyQueryService service = CreateService(database);

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month");

        Assert.Contains(overview.Warnings, warning => warning.Contains("折标煤系数"));
        ParkEnergyKpiDto steam = Assert.Single(overview.Kpis, kpi => kpi.Key == "steam");
        Assert.Equal("120", steam.Value);
        Assert.Contains("折标系数待配置", steam.Hint);
        ParkEnergyKpiDto total = Assert.Single(overview.Kpis, kpi => kpi.Key == "totalTce");
        Assert.Equal("0", total.Value);
    }

    [Fact]
    public async Task GetOverview_SecondaryEnergy_Should_Not_Enter_Total()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        Guid elecId = await AddMeterAsync(database, refs, "EM-E-001");
        Guid airId = await AddMeterAsync(
            database, refs, "EM-AIR-001", mediumId: refs.CoolingMediumId, isCumulative: true);

        DateOnly monthStart = MonthStart();
        foreach ((Guid id, decimal end) in new[] {(elecId, 1000m), (airId, 500m)})
        {
            await AddReadingAsync(database, id, At(monthStart, 1), 0m);
            await AddReadingAsync(database, id, At(monthStart, 2), end);
        }

        ParkEnergyQueryService service = CreateService(database);

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month");

        // 综合能耗只汇总一次能源；二次能源（冷量）单独作为分项，不编占比。
        ParkEnergyKpiDto total = Assert.Single(overview.Kpis, kpi => kpi.Key == "totalTce");
        Assert.Equal("0.12", total.Value);
        Assert.Single(overview.Kpis, kpi => kpi.Key == "elec");
        Assert.DoesNotContain(overview.Kpis, kpi => kpi.Key == "cooling");

        ParkEnergyShareDto secondary = Assert.Single(overview.SecondaryShares);
        Assert.Null(secondary.Share);
        Assert.Contains("折算", secondary.ShareText);
        Assert.DoesNotContain(overview.PrimaryShares, share => share.Name == "空调制冷量");
    }

    [Fact]
    public async Task GetOverview_OrgRows_Should_Rank_By_Tce()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);

        Guid deptId;
        await using (DataAcquisitionPlatformDbContext db = database.CreateContext())
        {
            EnergyOrgUnit dept = new()
            {
                Id = Guid.NewGuid(),
                Code = "DEPT-A",
                Name = "钞纸车间",
                Level = EnergyOrgUnit.DepartmentLevel,
                ParentId = refs.ParkOrgId,
                IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.EnergyOrgUnits.Add(dept);
            await db.SaveChangesAsync();
            deptId = dept.Id;
        }

        Guid bigId = await AddMeterAsync(database, refs, "EM-BIG", orgUnitId: refs.ParkOrgId);
        Guid smallId = await AddMeterAsync(database, refs, "EM-SMALL", orgUnitId: deptId);

        DateOnly monthStart = MonthStart();
        foreach ((Guid id, decimal end) in new[] {(bigId, 2000m), (smallId, 400m)})
        {
            await AddReadingAsync(database, id, At(monthStart, 1), 0m);
            await AddReadingAsync(database, id, At(monthStart, 2), end);
        }

        ParkEnergyQueryService service = CreateService(database);

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month");

        // 按折标量降序排名：园区大表在前，钞纸车间在后。
        Assert.Equal(2, overview.OrgRows.Count);
        Assert.Equal("园区", overview.OrgRows[0].Name);
        Assert.Equal("钞纸车间", overview.OrgRows[1].Name);
        Assert.True(overview.OrgRows[0].Share > overview.OrgRows[1].Share);
    }

    // ---------------------------------------------------------------
    // GetDictionaryAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetDictionary_Should_Return_Catalog_With_Sort_Order_And_Depth()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);

        Guid deptId;
        await using (DataAcquisitionPlatformDbContext db = database.CreateContext())
        {
            EnergyOrgUnit deptEntity = new()
            {
                Id = Guid.NewGuid(),
                Code = "DEPT-B",
                Name = "印钞车间",
                Level = EnergyOrgUnit.DepartmentLevel,
                ParentId = refs.ParkOrgId,
                IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            EnergyOrgUnit processEntity = new()
            {
                Id = Guid.NewGuid(),
                Code = "PROC-B",
                Name = "印刷工段",
                Level = EnergyOrgUnit.ProcessLevel,
                ParentId = deptEntity.Id,
                IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.EnergyOrgUnits.AddRange(deptEntity, processEntity);
            await db.SaveChangesAsync();
            deptId = deptEntity.Id;
        }

        ParkEnergyQueryService service = CreateService(database);

        EnergyDictionaryDto dictionary = await service.GetDictionaryAsync();

        Assert.Equal(5, dictionary.Media.Count);
        Assert.Equal(5, dictionary.UsageTypes.Count);
        Assert.Equal(5, dictionary.Systems.Count);

        // 介质按 SortOrder：电排第一，并带单位与折标系数。
        EnergyMediumDto elec = dictionary.Media[0];
        Assert.Equal("ELEC", elec.Code);
        Assert.Equal("kWh", elec.Unit);
        Assert.True(elec.IsPrimaryEnergy);
        Assert.Equal(0.1229m, elec.CoalFactor);

        // 层级深度在服务里算：园区 0、部门 1、工序 2。
        EnergyOrgUnitDto park = Assert.Single(dictionary.OrgUnits, unit => unit.Level == EnergyOrgUnit.ParkLevel);
        Assert.Equal(0, park.Depth);
        EnergyOrgUnitDto dept = Assert.Single(dictionary.OrgUnits, unit => unit.Id == deptId);
        Assert.Equal(1, dept.Depth);
        EnergyOrgUnitDto process = Assert.Single(dictionary.OrgUnits, unit => unit.Code == "PROC-B");
        Assert.Equal(2, process.Depth);
    }

    [Fact]
    public async Task GetDictionary_Should_Exclude_Disabled_Items()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);

        await using (DataAcquisitionPlatformDbContext db = database.CreateContext())
        {
            EnergyMedium water = await db.EnergyMedia.SingleAsync(item => item.Code == "WATER");
            water.IsEnabled = false;
            await db.SaveChangesAsync();
        }

        ParkEnergyQueryService service = CreateService(database);

        EnergyDictionaryDto dictionary = await service.GetDictionaryAsync();

        Assert.DoesNotContain(dictionary.Media, medium => medium.Code == "WATER");
        Assert.Equal(4, dictionary.Media.Count);
    }

    // ---------------------------------------------------------------
    // GetMetersAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetMeters_Should_Order_By_Code_And_Map_All_Dimensions()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);

        Guid parentCode = await AddMeterAsync(database, refs, "EM-PARENT");
        Guid childCode = await AddMeterAsync(database, refs, "EM-CHILD", parentMeterId: parentCode);
        await AddMeterAsync(database, refs, "EM-ALONE");

        ParkEnergyQueryService service = CreateService(database);

        IReadOnlyList<EnergyMeterDto> meters = await service.GetMetersAsync();

        Assert.Equal(3, meters.Count);
        Assert.Equal(new[] {"EM-ALONE", "EM-CHILD", "EM-PARENT"}, meters.Select(meter => meter.Code));

        EnergyMeterDto parent = meters.Single(meter => meter.Code == "EM-PARENT");
        Assert.Equal("ELEC", parent.MediumCode);
        Assert.Equal("kWh", parent.Unit);
        Assert.Equal("园区", parent.OrgUnitName);
        Assert.Equal("生产", parent.UsageTypeName);
        Assert.Equal("工艺设备", parent.SystemTypeName);
        Assert.True(parent.IsCumulative);
        Assert.Null(parent.ParentMeterCode);

        EnergyMeterDto child = meters.Single(meter => meter.Code == "EM-CHILD");
        Assert.Equal("EM-PARENT", child.ParentMeterCode);
    }

    // ---------------------------------------------------------------
    // UpsertMeterAsync
    // ---------------------------------------------------------------

    /// <summary>构造一个四维齐全的合法请求，各用例按需 with 覆盖。</summary>
    private static EnergyMeterUpsertRequest ValidMeterRequest(CatalogRefs refs, string code) => new(
        Id: null,
        Code: code,
        Name: $"{code} 名称",
        MediumId: refs.ElecMediumId,
        OrgUnitId: refs.ParkOrgId,
        UsageTypeId: refs.ProdUsageId,
        SystemTypeId: refs.ProcessSystemId,
        ParentMeterId: null,
        IsCumulative: true,
        Ratio: 1m,
        IsEnabled: true,
        Remark: null);

    [Fact]
    public async Task UpsertMeter_Create_Should_Persist_And_Return_Dto()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        // 编码 Trim 后落库；名称空白时回退为编码，备注 Trim。
        EnergyMeterDto dto = await service.UpsertMeterAsync(
            ValidMeterRequest(refs, "  EM-NEW-001  ") with {Name = "   ", Remark = "  备注  "});

        Assert.Equal("EM-NEW-001", dto.Code);
        Assert.Equal("EM-NEW-001", dto.Name);
        Assert.Equal("备注", dto.Remark);
        Assert.Equal("ELEC", dto.MediumCode);
        Assert.True(dto.IsEnabled);

        await using (DataAcquisitionPlatformDbContext db = database.CreateContext())
        {
            EnergyMeter entity = await db.EnergyMeters.SingleAsync(meter => meter.Code == "EM-NEW-001");
            Assert.Equal(dto.Id, entity.Id);
            Assert.Equal("EM-NEW-001", entity.Name);
            Assert.Equal("备注", entity.Remark);
        }
    }

    [Fact]
    public async Task UpsertMeter_Update_Should_Change_Fields_Keep_Id()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        Guid meterId = await AddMeterAsync(database, refs, "EM-UP-001");
        ParkEnergyQueryService service = CreateService(database);

        EnergyMeterDto dto = await service.UpsertMeterAsync(
            ValidMeterRequest(refs, "EM-UP-001") with
            {
                Id = meterId, Name = "新名字", Ratio = 10m, Remark = "  备注  ",
            });

        Assert.Equal(meterId, dto.Id);
        Assert.Equal("新名字", dto.Name);
        Assert.Equal(10m, dto.Ratio);
        Assert.Equal("备注", dto.Remark);

        await using (DataAcquisitionPlatformDbContext db = database.CreateContext())
        {
            EnergyMeter entity = await db.EnergyMeters.SingleAsync(meter => meter.Id == meterId);
            Assert.Equal("新名字", entity.Name);
            Assert.Equal(10m, entity.Ratio);
            Assert.Equal("备注", entity.Remark);
            Assert.Equal("EM-UP-001", entity.Code);
        }
    }

    [Fact]
    public async Task UpsertMeter_BlankCode_Should_Be_Rejected()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertMeterAsync(ValidMeterRequest(refs, "   ")));

        Assert.Contains("计量点编码不能为空", ex.Message);
    }

    [Fact]
    public async Task UpsertMeter_NonPositiveRatio_Should_Be_Rejected()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertMeterAsync(ValidMeterRequest(refs, "EM-R0") with {Ratio = 0m}));

        Assert.Contains("倍率必须大于 0", ex.Message);
    }

    [Fact]
    public async Task UpsertMeter_DuplicatedCode_Should_Be_Rejected()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        await AddMeterAsync(database, refs, "EM-DUP");
        ParkEnergyQueryService service = CreateService(database);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertMeterAsync(ValidMeterRequest(refs, "EM-DUP")));

        Assert.Contains("已存在", ex.Message);
    }

    [Fact]
    public async Task UpsertMeter_SelfParent_Should_Be_Rejected()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        Guid meterId = await AddMeterAsync(database, refs, "EM-SELF");
        ParkEnergyQueryService service = CreateService(database);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertMeterAsync(ValidMeterRequest(refs, "EM-SELF") with
            {
                Id = meterId, ParentMeterId = meterId,
            }));

        Assert.Contains("上级计量点不能是自身", ex.Message);
    }

    [Fact]
    public async Task UpsertMeter_MissingParent_Should_Be_Rejected()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertMeterAsync(ValidMeterRequest(refs, "EM-ORPH") with
            {
                ParentMeterId = Guid.NewGuid(),
            }));

        Assert.Contains("上级计量点不存在", ex.Message);
    }

    [Fact]
    public async Task UpsertMeter_ParentWithDifferentMedium_Should_Be_Rejected()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        Guid parentId = await AddMeterAsync(database, refs, "EM-P");
        ParkEnergyQueryService service = CreateService(database);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertMeterAsync(ValidMeterRequest(refs, "EM-C") with
            {
                MediumId = refs.WaterMediumId, ParentMeterId = parentId,
            }));

        Assert.Contains("上级计量点必须使用相同介质", ex.Message);
    }

    [Fact]
    public async Task UpsertMeter_CyclicParent_Should_Be_Rejected()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);

        Guid parentId = await AddMeterAsync(database, refs, "EM-PARENT");
        Guid childId = await AddMeterAsync(database, refs, "EM-CHILD", parentMeterId: parentId);

        ParkEnergyQueryService service = CreateService(database);

        // 把上级表挂到自己的下级：B 的上级改成 A（A 的上级是 B），形成环。
        // 注意请求编码必须是被更新实体自己的编码，否则会先撞「编码已存在」。
        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertMeterAsync(ValidMeterRequest(refs, "EM-PARENT") with
            {
                Id = parentId, ParentMeterId = childId,
            }));

        Assert.Contains("不能是自身或下级计量点", ex.Message);
    }

    [Fact]
    public async Task UpsertMeter_ChildrenWithDifferentMedium_Should_Block_Medium_Change()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        Guid parentId = await AddMeterAsync(database, refs, "EM-PARENT");
        await AddMeterAsync(database, refs, "EM-CHILD", parentMeterId: parentId);
        ParkEnergyQueryService service = CreateService(database);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertMeterAsync(ValidMeterRequest(refs, "EM-PARENT") with
            {
                Id = parentId, MediumId = refs.WaterMediumId,
            }));

        Assert.Contains("存在不同介质的下级计量点", ex.Message);
    }

    [Theory]
    [InlineData("介质不存在")]
    [InlineData("组织节点不存在")]
    [InlineData("用能用途不存在")]
    [InlineData("设备系统不存在")]
    public async Task UpsertMeter_UnknownDimension_Should_Be_Rejected_With_Chinese_Hint(string expected)
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        EnergyMeterUpsertRequest request = expected switch
        {
            "介质不存在" => ValidMeterRequest(refs, "EM-X") with {MediumId = Guid.NewGuid()},
            "组织节点不存在" => ValidMeterRequest(refs, "EM-X") with {OrgUnitId = Guid.NewGuid()},
            "用能用途不存在" => ValidMeterRequest(refs, "EM-X") with {UsageTypeId = Guid.NewGuid()},
            _ => ValidMeterRequest(refs, "EM-X") with {SystemTypeId = Guid.NewGuid()},
        };

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() => service.UpsertMeterAsync(request));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task UpsertMeter_UnknownId_Should_Be_Rejected()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertMeterAsync(ValidMeterRequest(refs, "EM-GONE") with {Id = Guid.NewGuid()}));

        Assert.Contains("计量点不存在", ex.Message);
    }

    // ---------------------------------------------------------------
    // DeleteMeterAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task DeleteMeter_UnknownId_Should_Return_False()
    {
        using EnergySqliteTestDatabase database = new();
        ParkEnergyQueryService service = CreateService(database);

        Assert.False(await service.DeleteMeterAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteMeter_WithReadings_Should_Be_Rejected()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        Guid meterId = await AddMeterAsync(database, refs, "EM-DEL-1");
        await AddReadingAsync(database, meterId, DateTimeOffset.UtcNow, 100m);
        ParkEnergyQueryService service = CreateService(database);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteMeterAsync(meterId));

        // 有读数的表删掉会让历史数据失去归属：正确做法是停用。
        Assert.Contains("已有读数", ex.Message);
        Assert.Contains("停用", ex.Message);
    }

    [Fact]
    public async Task DeleteMeter_WithChildren_Should_Be_Rejected()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        Guid parentId = await AddMeterAsync(database, refs, "EM-DEL-P");
        await AddMeterAsync(database, refs, "EM-DEL-C", parentMeterId: parentId);
        ParkEnergyQueryService service = CreateService(database);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteMeterAsync(parentId));

        Assert.Contains("下级表", ex.Message);
    }

    [Fact]
    public async Task DeleteMeter_CleanMeter_Should_Remove()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        Guid meterId = await AddMeterAsync(database, refs, "EM-DEL-OK");
        ParkEnergyQueryService service = CreateService(database);

        Assert.True(await service.DeleteMeterAsync(meterId));

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Assert.False(await db.EnergyMeters.AnyAsync(meter => meter.Id == meterId));
        Assert.False(await service.DeleteMeterAsync(meterId));
    }

    // ---------------------------------------------------------------
    // UpsertOrgUnitAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task UpsertOrgUnit_DepartmentTree_Should_Compute_Depth()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        EnergyOrgUnitDto dept = await service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
            null, " DEPT-T-1 ", "动力车间", EnergyOrgUnit.DepartmentLevel, refs.ParkOrgId));
        EnergyOrgUnitDto process = await service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
            null, "PROC-T-1", "空压站", EnergyOrgUnit.ProcessLevel, dept.Id));

        Assert.Equal(1, dept.Depth);
        Assert.Equal(2, process.Depth);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Assert.True(await db.EnergyOrgUnits.AnyAsync(unit => unit.Code == "DEPT-T-1" && unit.IsEnabled));
    }

    [Fact]
    public async Task UpsertOrgUnit_Park_Should_Require_No_Parent()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        EnergyOrgUnitDto park = await service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
            null, "PARK-2", "二期园区", EnergyOrgUnit.ParkLevel, null));
        Assert.Equal(0, park.Depth);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
                null, "PARK-3", "三期园区", EnergyOrgUnit.ParkLevel, refs.ParkOrgId)));
        Assert.Contains("园区级节点不能有上级", ex.Message);
    }

    [Theory]
    [InlineData(EnergyOrgUnit.DepartmentLevel, null, "部门与工序级节点必须选择上级节点")]
    [InlineData("team", null, "节点层级只能是园区、部门或工序")]
    public async Task UpsertOrgUnit_InvalidShape_Should_Be_Rejected(string level, Guid? parentId, string expected)
    {
        using EnergySqliteTestDatabase database = new();
        await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
                null, "ORG-X", "节点", level, parentId)));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task UpsertOrgUnit_LevelParentMismatch_Should_Be_Rejected()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        // 部门的上级必须是园区。
        ArgumentException unknownParent = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
                null, "DEPT-BAD", "节点", EnergyOrgUnit.DepartmentLevel, Guid.NewGuid())));
        Assert.Contains("上级节点不存在", unknownParent.Message);

        EnergyOrgUnitDto deptOk = await service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
            null, "DEPT-OK", "节点", EnergyOrgUnit.DepartmentLevel, refs.ParkOrgId));

        // 工序的上级必须是部门。
        ArgumentException processUnderPark = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
                null, "PROC-BAD", "节点", EnergyOrgUnit.ProcessLevel, refs.ParkOrgId)));
        Assert.Contains("工序级节点的上级必须是部门", processUnderPark.Message);

        // 部门的上级不能是部门。
        ArgumentException deptUnderDept = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
                null, "DEPT-BAD-2", "节点", EnergyOrgUnit.DepartmentLevel, deptOk.Id)));
        Assert.Contains("部门级节点的上级必须是园区", deptUnderDept.Message);
    }

    [Fact]
    public async Task UpsertOrgUnit_BlankCodeOrName_Should_Be_Rejected()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
                null, "  ", "节点", EnergyOrgUnit.DepartmentLevel, Guid.NewGuid())));

        Assert.Contains("节点编码与名称都不能为空", ex.Message);
    }

    [Fact]
    public async Task UpsertOrgUnit_DuplicatedCode_Should_Be_Rejected()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
                null, "PARK", "园区二号楼", EnergyOrgUnit.ParkLevel, null)));

        Assert.Contains("节点编码 PARK 已存在", ex.Message);
    }

    [Fact]
    public async Task UpsertOrgUnit_CyclicParent_Should_Be_Rejected()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        EnergyOrgUnitDto dept = await service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
            null, "DEPT-C", "车间", EnergyOrgUnit.DepartmentLevel, refs.ParkOrgId));
        EnergyOrgUnitDto process = await service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
            null, "PROC-C", "工段", EnergyOrgUnit.ProcessLevel, dept.Id));

        // 常规路径造不出环（层级校验在先、数据库还有 CHECK 兜底），
        // 但「把节点的上级设成它自己」这条守卫必须仍然成立：
        // 这里以部门身份更新园区节点、并把上级指向它自己。
        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
                refs.ParkOrgId, "DEPT-CYC", "园区被当作部门", EnergyOrgUnit.DepartmentLevel, refs.ParkOrgId)));

        Assert.Contains("上级节点不能是自己或自己的下级", ex.Message);
    }

    [Fact]
    public async Task UpsertOrgUnit_Update_Should_Rename_Keep_Level()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        EnergyOrgUnitDto dept = await service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
            null, "DEPT-R", "旧名", EnergyOrgUnit.DepartmentLevel, refs.ParkOrgId));

        EnergyOrgUnitDto renamed = await service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
            dept.Id, "DEPT-R", "新名", EnergyOrgUnit.DepartmentLevel, refs.ParkOrgId));

        Assert.Equal(dept.Id, renamed.Id);
        Assert.Equal("新名", renamed.Name);

        // 用全新编码更新一个不存在的标识：编码查重先通过，才轮到「节点不存在」。
        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpsertOrgUnitAsync(new EnergyOrgUnitUpsertRequest(
                Guid.NewGuid(), "DEPT-R-2", "不存在", EnergyOrgUnit.DepartmentLevel, refs.ParkOrgId)));
        Assert.Contains("节点不存在", ex.Message);
    }

    // ---------------------------------------------------------------
    // UpdateMediumFactorAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task UpdateMediumFactor_Should_Persist_Value_And_Allow_Clear()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        // 蒸汽默认没有系数：按焓值配置 92 kgce/t。
        EnergyMediumDto set = await service.UpdateMediumFactorAsync(refs.SteamMediumId, 92m);
        Assert.Equal(92m, set.CoalFactor);

        await using (DataAcquisitionPlatformDbContext db = database.CreateContext())
        {
            EnergyMedium steam = await db.EnergyMedia.SingleAsync(item => item.Id == refs.SteamMediumId);
            Assert.Equal(92m, steam.CoalFactor);
        }

        // 清除系数：不同批次热值不同，允许恢复为空。
        EnergyMediumDto cleared = await service.UpdateMediumFactorAsync(refs.SteamMediumId, null);
        Assert.Null(cleared.CoalFactor);
    }

    [Fact]
    public async Task UpdateMediumFactor_InvalidInput_Should_Be_Rejected()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        ParkEnergyQueryService service = CreateService(database);

        ArgumentException unknown = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateMediumFactorAsync(Guid.NewGuid(), 1m));
        Assert.Contains("介质不存在", unknown.Message);

        ArgumentException negative = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateMediumFactorAsync(refs.ElecMediumId, -0.1m));
        Assert.Contains("不能为负数", negative.Message);

        // 二次能源没有折标煤口径：配了系数会让人以为它进了综合能耗。
        ArgumentException secondary = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateMediumFactorAsync(refs.CoolingMediumId, 1m));
        Assert.Contains("不是一次能源", secondary.Message);
    }

    [Fact]
    public async Task UpdateMediumFactor_ClearedFactor_Should_Reappear_In_Overview_Warning()
    {
        using EnergySqliteTestDatabase database = new();
        CatalogRefs refs = await SeedCatalogAsync(database);
        Guid meterId = await AddMeterAsync(database, refs, "EM-FACTOR");

        DateOnly monthStart = MonthStart();
        await AddReadingAsync(database, meterId, At(monthStart.AddDays(-1), 12), 0m);
        await AddReadingAsync(database, meterId, At(monthStart, 1), 1000m);

        ParkEnergyQueryService service = CreateService(database);

        // 清掉电的折标系数后，总览必须提示「未计入综合能耗」而不是默默少一块。
        await service.UpdateMediumFactorAsync(refs.ElecMediumId, null);

        ParkEnergyOverviewDto overview = await service.GetOverviewAsync("month");

        Assert.Contains(overview.Warnings, warning => warning.Contains("折标煤系数"));
        ParkEnergyKpiDto total = Assert.Single(overview.Kpis, kpi => kpi.Key == "totalTce");
        Assert.Equal("0", total.Value);
    }

    /// <summary>字典播种后各维度的常用标识。</summary>
    private sealed record CatalogRefs(
        Guid ElecMediumId,
        Guid WaterMediumId,
        Guid SteamMediumId,
        Guid CoolingMediumId,
        Guid ProdUsageId,
        Guid ProcessSystemId,
        Guid ParkOrgId);
}

/// <summary>
/// 面向园区能源测试的 SQLite 内存库。
///
/// 与共享的 <c>SqliteTestDatabase</c>（TestDoubles 目录）相比多了一步归一化：EF Core 的 SQLite
/// 提供程序不支持 DateTimeOffset 的比较 / 排序翻译，而能源服务要按
/// <c>EnergyReading.ReadAt</c> 过滤期间读数（差分基线、期间窗口），
/// 不处理就无法把总览聚合纳入测试。这里把全部 DateTimeOffset 属性改为
/// Unix 毫秒整数存储：比较与排序可翻译，时刻语义（按 UTC 即时点比较）不变。
/// 读回值统一带 +00:00 偏移，DateTimeOffset 的等值判定按时间点比较，断言不受影响。
/// </summary>
internal sealed class EnergySqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>创建内存库并建表。连接在工厂生命周期内保持打开，多个 DbContext 共享同一份数据。</summary>
    public EnergySqliteTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using DataAcquisitionPlatformDbContext context = CreateContext();
        context.Database.EnsureCreated();
    }

    /// <summary>创建指向同一内存库的新 DbContext，贴近真实请求边界。</summary>
    public DataAcquisitionPlatformDbContext CreateContext()
    {
        DbContextOptionsBuilder<DataAcquisitionPlatformDbContext> builder = new();
        builder
            .UseSqlite(_connection)
            .ReplaceService<IModelCustomizer, EnergySqliteModelCustomizer>();

        return new DataAcquisitionPlatformDbContext(builder.Options);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}

/// <summary>
/// 在共享归一化逻辑（btrim 改写、PG 默认值剥离、jsonb 退化 TEXT）的基础上，
/// 额外把 DateTimeOffset 属性换成 Unix 毫秒值转换器。
/// </summary>
internal sealed class EnergySqliteModelCustomizer(ModelCustomizerDependencies dependencies)
    : ModelCustomizer(dependencies)
{
    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        // 先照常执行 DataAcquisitionPlatformDbContext.OnModelCreating（含全部实体配置）。
        base.Customize(modelBuilder, context);

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            // CHECK 约束里的 btrim() 是 PostgreSQL 函数，SQLite 同义函数是 trim()。
            foreach (IReadOnlyCheckConstraint checkConstraint in entityType.GetCheckConstraints().ToList())
            {
                string? rewritten = checkConstraint.Sql?.Replace("btrim(", "trim(", StringComparison.Ordinal);
                if (rewritten is null || rewritten == checkConstraint.Sql) continue;

                entityType.RemoveCheckConstraint(checkConstraint.Name!);
                entityType.AddCheckConstraint(checkConstraint.Name!, rewritten);
            }

            foreach (IMutableProperty property in entityType.GetProperties())
            {
                property.SetDefaultValueSql(null);

                if (property.GetColumnType() == "jsonb")
                    property.SetColumnType("TEXT");

                if (property.ClrType == typeof(DateTimeOffset))
                {
                    // Unix 毫秒：整列可比较可排序，且与时区偏移无关（即时间点语义）。
                    property.SetValueConverter(
                        new ValueConverter<DateTimeOffset, long>(
                            value => value.ToUnixTimeMilliseconds(),
                            value => DateTimeOffset.FromUnixTimeMilliseconds(value)));
                }
            }
        }
    }
}

/// <summary>指向 <see cref="EnergySqliteTestDatabase"/> 的作用域工厂，与 HostingFakes 的注册方式一致。</summary>
internal sealed class EnergyScopeFactory(EnergySqliteTestDatabase database) : IServiceScopeFactory
{
    private readonly ServiceProvider _provider = Build(database);

    public IServiceScope CreateScope()
    {
        return _provider.CreateScope();
    }

    private static ServiceProvider Build(EnergySqliteTestDatabase database)
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.ClearProviders());
        services.AddScoped<DataAcquisitionPlatformDbContext>(_ => database.CreateContext());
        return services.BuildServiceProvider();
    }
}
