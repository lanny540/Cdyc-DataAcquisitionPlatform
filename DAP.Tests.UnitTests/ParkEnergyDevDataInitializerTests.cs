using DAP.Core.Domain.Entities;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Services;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 园区能源开发种子数据初始化器的行为测试。
///
/// 钉住三条关键约束：开关语义（Development 默认开、其他环境默认关、
/// Energy:SeedDevData 可显式覆盖）、幂等（库里已有计量点就整体跳过，
/// 用户的修改永远不会被重启盖回去）、以及 730 天回填与示数单调性
/// （示数不单调会让差分聚合整月按 0 计）。
/// </summary>
public sealed class ParkEnergyDevDataInitializerTests
{
    /// <summary>先铺字典（介质 / 用途 / 系统 / 园区根节点），种子数据依赖它。</summary>
    private static async Task SeedDictionaryAsync(EnergySqliteTestDatabase database)
    {
        await new EnergyDictionaryInitializer(
                new EnergyScopeFactory(database),
                NullLogger<EnergyDictionaryInitializer>.Instance)
            .StartAsync(CancellationToken.None);
    }

    private static ParkEnergyDevDataInitializer CreateInitializer(
        EnergySqliteTestDatabase database,
        Microsoft.Extensions.Hosting.IHostEnvironment? environment = null,
        params (string Key, string? Value)[] config)
    {
        return new ParkEnergyDevDataInitializer(
            new EnergyScopeFactory(database),
            HostingFakes.CreateConfiguration(config),
            environment ?? HostingFakes.CreateDevelopmentEnvironment(),
            NullLogger<ParkEnergyDevDataInitializer>.Instance);
    }

    [Fact]
    public async Task StartAsync_InDevelopment_Should_Seed_OrgTree_Meters_And_Readings()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedDictionaryAsync(database);

        await CreateInitializer(database).StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        // 11 块计量点：6 电表 + 2 蒸汽 + 1 水 + 1 压缩空气 + 1 冷量。
        Assert.Equal(11, await db.EnergyMeters.CountAsync());
        Assert.Contains(await db.EnergyMeters.Select(meter => meter.Code).ToListAsync(),
            code => code == "ELEC-PAPER");

        // 组织树：园区根节点 + 6 部门 + 7 工序 = 14。
        Assert.Equal(14, await db.EnergyOrgUnits.CountAsync());
        Assert.True(await db.EnergyOrgUnits.AnyAsync(unit => unit.Code == "PROC-POWER-AIR"));

        // 蒸汽补了开发占位折标系数 92 kgce/t。
        EnergyMedium steam = await db.EnergyMedia.SingleAsync(item => item.Code == "STEAM");
        Assert.Equal(92m, steam.CoalFactor);
    }

    [Fact]
    public async Task StartAsync_Should_Backfill_730_Days_With_Monotonic_Cumulative_Readings()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedDictionaryAsync(database);

        await CreateInitializer(database).StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();

        // 日抄表：730 天回填（含今天），每天一条区间读数，共 729 条。
        Guid steamMeterId = await db.EnergyMeters
            .Where(meter => meter.Code == "STEAM-PAPER")
            .Select(meter => meter.Id)
            .SingleAsync();

        List<EnergyReading> steamReadings = await db.EnergyReadings
            .Where(reading => reading.MeterId == steamMeterId)
            .OrderBy(reading => reading.ReadAt)
            .ToListAsync();
        Assert.Equal(729, steamReadings.Count);

        // 累计表示数必须单调递增：否则月度差分会出现负值，整块表按 0 计。
        Guid elecMeterId = await db.EnergyMeters
            .Where(meter => meter.Code == "ELEC-PAPER")
            .Select(meter => meter.Id)
            .SingleAsync();

        List<EnergyReading> elecReadings = await db.EnergyReadings
            .Where(reading => reading.MeterId == elecMeterId)
            .OrderBy(reading => reading.ReadAt)
            .ToListAsync();
        Assert.True(elecReadings.Count > 729); // 电表最近 3 天按小时抄见，条数更多。
        for (int index = 1; index < elecReadings.Count; index++)
        {
            Assert.True(elecReadings[index].Value >= elecReadings[index - 1].Value,
                $"累计示数在第 {index} 条出现回退");
        }

        // 非累计表（积算仪）读数即区间用量，量级不应超过日均用量太多。
        Guid airMeterId = await db.EnergyMeters
            .Where(meter => meter.Code == "AIR-POWER")
            .Select(meter => meter.Id)
            .SingleAsync();
        List<decimal> airValues = await db.EnergyReadings
            .Where(reading => reading.MeterId == airMeterId)
            .Select(reading => reading.Value)
            .ToListAsync();
        Assert.Equal(729, airValues.Count);
        Assert.All(airValues, value => Assert.True(value > 0m && value < 21_000m * 1.2m));

        // 每块表都有读数。
        Assert.Equal(11, await db.EnergyReadings
            .GroupBy(reading => reading.MeterId)
            .Select(group => group.Key)
            .CountAsync());
    }

    [Fact]
    public async Task StartAsync_Twice_Should_Skip_Second_Run()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedDictionaryAsync(database);

        await CreateInitializer(database).StartAsync(CancellationToken.None);

        int meterCount;
        int readingCount;
        await using (DataAcquisitionPlatformDbContext db = database.CreateContext())
        {
            meterCount = await db.EnergyMeters.CountAsync();
            readingCount = await db.EnergyReadings.CountAsync();
        }

        // 二次执行：已有计量点 → 整体跳过，用户手工补的台账与读数不会被盖回去。
        await CreateInitializer(database).StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db2 = database.CreateContext();
        Assert.Equal(meterCount, await db2.EnergyMeters.CountAsync());
        Assert.Equal(readingCount, await db2.EnergyReadings.CountAsync());
    }

    [Fact]
    public async Task StartAsync_WithExistingMeter_Should_Not_Seed_Anything()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedDictionaryAsync(database);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using (DataAcquisitionPlatformDbContext db = database.CreateContext())
        {
            EnergyMedium elec = await db.EnergyMedia.SingleAsync(item => item.Code == "ELEC");
            EnergyUsageType usage = await db.EnergyUsageTypes.SingleAsync(item => item.Code == "PROD");
            EnergySystemType system = await db.EnergySystems.SingleAsync(item => item.Code == "PROCESS");
            EnergyOrgUnit park = await db.EnergyOrgUnits.SingleAsync(unit => unit.Level == EnergyOrgUnit.ParkLevel);

            db.EnergyMeters.Add(new EnergyMeter
            {
                Id = Guid.NewGuid(),
                Code = "EM-MANUAL-001",
                Name = "手工台账",
                MediumId = elec.Id,
                OrgUnitId = park.Id,
                UsageTypeId = usage.Id,
                SystemTypeId = system.Id,
                Ratio = 1m,
                IsCumulative = true,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        await CreateInitializer(database).StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db2 = database.CreateContext();
        Assert.Equal(1, await db2.EnergyMeters.CountAsync());
        // 组织树同样不补：种子判定在读组织树之前。
        Assert.Equal(1, await db2.EnergyOrgUnits.CountAsync());
        Assert.Equal(0, await db2.EnergyReadings.CountAsync());
    }

    [Fact]
    public async Task StartAsync_InProduction_Default_Should_Not_Seed()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedDictionaryAsync(database);

        // 其他环境默认不执行：测试数据不能混进正式库。
        await CreateInitializer(database, HostingFakes.CreateProductionEnvironment())
            .StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Assert.Equal(0, await db.EnergyMeters.CountAsync());
        Assert.Equal(0, await db.EnergyReadings.CountAsync());
    }

    [Fact]
    public async Task StartAsync_ConfigSwitch_Should_Override_Environment()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedDictionaryAsync(database);

        // 开发环境 + 显式关闭 → 不种子。
        await CreateInitializer(database, HostingFakes.CreateDevelopmentEnvironment(),
                ("Energy:SeedDevData", "false"))
            .StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Assert.Equal(0, await db.EnergyMeters.CountAsync());

        // 生产环境 + 显式开启 → 种子。
        using EnergySqliteTestDatabase database2 = new();
        await SeedDictionaryAsync(database2);
        await CreateInitializer(database2, HostingFakes.CreateProductionEnvironment(),
                ("Energy:SeedDevData", "true"))
            .StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db2 = database2.CreateContext();
        Assert.Equal(11, await db2.EnergyMeters.CountAsync());
    }

    [Fact]
    public async Task StartAsync_WithoutParkRoot_Should_Skip_Without_Throwing()
    {
        using EnergySqliteTestDatabase database = new();
        // 不铺字典：没有园区根节点，种子无从挂起，但也不能把宿主拖挂。

        await CreateInitializer(database).StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Assert.Equal(0, await db.EnergyMeters.CountAsync());
        Assert.Equal(0, await db.EnergyReadings.CountAsync());
    }

    [Fact]
    public async Task StartAsync_SteamFactor_Should_Not_Override_User_Value()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedDictionaryAsync(database);

        // 用户先按实际焓值配好蒸汽系数：种子「只补不覆盖」。
        await using (DataAcquisitionPlatformDbContext db = database.CreateContext())
        {
            EnergyMedium steam = await db.EnergyMedia.SingleAsync(item => item.Code == "STEAM");
            steam.CoalFactor = 80m;
            steam.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        await CreateInitializer(database).StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db2 = database.CreateContext();
        EnergyMedium steam2 = await db2.EnergyMedia.SingleAsync(item => item.Code == "STEAM");
        Assert.Equal(80m, steam2.CoalFactor);
    }

    [Fact]
    public async Task StartAsync_IncompleteDictionary_Should_Create_No_Meters_Without_Throwing()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedDictionaryAsync(database);

        // 字典被删空（现场改编码 / 停用后重启）：少一块表比挂错维度好排查。
        await using (DataAcquisitionPlatformDbContext db = database.CreateContext())
        {
            db.EnergyUsageTypes.RemoveRange(db.EnergyUsageTypes);
            await db.SaveChangesAsync();
        }

        await CreateInitializer(database).StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db2 = database.CreateContext();
        Assert.Equal(0, await db2.EnergyMeters.CountAsync());
        Assert.Equal(0, await db2.EnergyReadings.CountAsync());
    }

    [Fact]
    public async Task StartAsync_SeededData_Should_Produce_Live_Overview()
    {
        using EnergySqliteTestDatabase database = new();
        await SeedDictionaryAsync(database);

        await CreateInitializer(database).StartAsync(CancellationToken.None);

        // 种子走的是与正式环境完全相同的聚合链路：页面按 live 渲染才对。
        ParkEnergyQueryService queryService = new(
            database.CreateContext(),
            HostingFakes.CreateConfiguration(),
            HostingFakes.CreateDevelopmentEnvironment(),
            NullLogger<ParkEnergyQueryService>.Instance);

        DAP.Core.Shared.Contracts.ParkEnergyOverviewDto overview =
            await queryService.GetOverviewAsync("month");

        Assert.Equal(DAP.Core.Shared.Contracts.ParkEnergyDataStatus.Live, overview.DataStatus);
        Assert.NotNull(overview.DataThrough);
        Assert.Equal(6, await CountEnabledElecMetersAsync(database));
        Assert.Contains(overview.Kpis, kpi => kpi.Key == "totalTce" && kpi.Value != "0");

        int elecKpiCount = overview.Kpis.Count(kpi => kpi.Key == "elec");
        Assert.Equal(1, elecKpiCount);
        Assert.True(overview.OrgRows.Count >= 1, "种子数据应能按部门出组织占比");
    }

    private static async Task<int> CountEnabledElecMetersAsync(EnergySqliteTestDatabase database)
    {
        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        return await db.EnergyMeters.CountAsync(meter => meter.IsEnabled && meter.Code.StartsWith("ELEC-"));
    }
}
