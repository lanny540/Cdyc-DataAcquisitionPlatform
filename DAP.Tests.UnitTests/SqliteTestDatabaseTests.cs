using DAP.Core.Domain.Entities;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Services;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAP.Tests.UnitTests;

/// <summary>
/// SQLite 内存测试库的连通性冒烟测试。
///
/// 这些用例证明：生产模型在归一化后可在 SQLite 内存库建表、写入并跨上下文读回；
/// 依赖 DbContext / IServiceScopeFactory 的初始化器可以真实执行。
/// 所有服务级单元测试都以本能力为前提。
/// </summary>
public sealed class SqliteTestDatabaseTests
{
    [Fact]
    public async Task EnsureCreated_Should_Create_All_Tables()
    {
        using SqliteTestDatabase database = new();

        await using DataAcquisitionPlatformDbContext context = database.CreateContext();

        // 表存在性由「空表可查询」验证；模型条目数兜底防止建表被静默截断。
        Assert.Empty(await context.AppUsers.ToListAsync());
        Assert.Empty(await context.EnergyMeters.ToListAsync());
        Assert.Empty(await context.EnergyReadings.ToListAsync());
        Assert.Empty(await context.ManagedDataDefinitions.ToListAsync());
        Assert.True(context.Model.GetEntityTypes().Count() >= 20);
    }

    [Fact]
    public async Task DictionaryInitializer_Should_Seed_Energy_Catalog_Once()
    {
        using SqliteTestDatabase database = new();

        EnergyDictionaryInitializer initializer = new(
            HostingFakes.CreateScopeFactory(database),
            NullLogger<EnergyDictionaryInitializer>.Instance);

        await initializer.StartAsync(CancellationToken.None);

        await using (DataAcquisitionPlatformDbContext context = database.CreateContext())
        {
            // 内置字典：介质（电/水/蒸汽等）+ 用途 5 类 + 系统 5 类 + 园区根节点。
            Assert.True(await context.EnergyMedia.AnyAsync(item => item.Code == "ELEC"));
            Assert.True(await context.EnergyUsageTypes.AnyAsync(item => item.Code == "PROD"));
            Assert.True(await context.EnergySystems.AnyAsync(item => item.Code == "LIGHTING"));
            Assert.True(await context.EnergyOrgUnits.AnyAsync(item => item.Level == EnergyOrgUnit.ParkLevel));
        }

        // 「只补不覆盖」：重复执行不再新增。
        await initializer.StartAsync(CancellationToken.None);
        await using (DataAcquisitionPlatformDbContext context = database.CreateContext())
        {
            Assert.Equal(
                await context.EnergyMedia.CountAsync(),
                (await context.EnergyMedia.Select(item => item.Code).ToListAsync()).Distinct().Count());
        }
    }

    [Fact]
    public async Task DateTimeOffset_And_Decimal_Should_RoundTrip_Through_Meter_Chain()
    {
        using SqliteTestDatabase database = new();

        // 先补字典与园区节点，再沿外键链建表计量点。
        await new EnergyDictionaryInitializer(
                HostingFakes.CreateScopeFactory(database),
                NullLogger<EnergyDictionaryInitializer>.Instance)
            .StartAsync(CancellationToken.None);

        DateTimeOffset occurredAt = new(2026, 11, 19, 8, 30, 0, TimeSpan.FromHours(8));
        decimal value = 1234.5678m;

        Guid meterId;
        await using (DataAcquisitionPlatformDbContext context = database.CreateContext())
        {
            EnergyMedium medium = await context.EnergyMedia.SingleAsync(item => item.Code == "ELEC");
            EnergyUsageType usage = await context.EnergyUsageTypes.SingleAsync(item => item.Code == "PROD");
            EnergySystemType system = await context.EnergySystems.SingleAsync(item => item.Code == "PROCESS");
            EnergyOrgUnit park = await context.EnergyOrgUnits.SingleAsync(item => item.Level == EnergyOrgUnit.ParkLevel);

            EnergyMeter meter = new()
            {
                Id = Guid.NewGuid(),
                Code = "EM-TEST-001",
                Name = "测试电表",
                MediumId = medium.Id,
                OrgUnitId = park.Id,
                UsageTypeId = usage.Id,
                SystemTypeId = system.Id,
                Ratio = 10m,
                IsCumulative = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            context.EnergyMeters.Add(meter);
            meterId = meter.Id;
            await context.SaveChangesAsync();
        }

        await using (DataAcquisitionPlatformDbContext context = database.CreateContext())
        {
            context.EnergyReadings.Add(new EnergyReading
            {
                Id = Guid.NewGuid(),
                MeterId = meterId,
                ReadAt = occurredAt,
                Value = value,
                Source = EnergyReading.ManualSource,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        await using (DataAcquisitionPlatformDbContext context = database.CreateContext())
        {
            EnergyReading? reading = await context.EnergyReadings
                .SingleOrDefaultAsync(item => item.MeterId == meterId);
            Assert.NotNull(reading);
            Assert.Equal(occurredAt, reading.ReadAt);
            Assert.Equal(value, reading.Value);
        }
    }
}
