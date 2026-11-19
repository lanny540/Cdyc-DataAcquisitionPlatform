using DAP.Core.Domain.Entities;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Services;
using DAP.Tests.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 能源字典初始化器的行为分支测试（基础播种用例见 <c>SqliteTestDatabaseTests</c>）。
///
/// 这里钉住的是「只补不覆盖」的语义细节：编码查重的大小写不敏感、
/// 用户改过的名称与启停状态不被重启盖回、只补缺失项、园区根节点不重复建，
/// 以及初始化失败不阻断宿主启动。
/// </summary>
public sealed class EnergyDictionaryInitializerTests
{
    private static EnergyDictionaryInitializer CreateInitializer(EnergySqliteTestDatabase database)
    {
        return new EnergyDictionaryInitializer(
            new EnergyScopeFactory(database),
            NullLogger<EnergyDictionaryInitializer>.Instance);
    }

    [Fact]
    public async Task StartAsync_EmptyDatabase_Should_Add_Full_Catalog()
    {
        using EnergySqliteTestDatabase database = new();

        await CreateInitializer(database).StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db = database.CreateContext();
        Assert.Equal(5, await db.EnergyMedia.CountAsync());
        Assert.Equal(5, await db.EnergyUsageTypes.CountAsync());
        Assert.Equal(5, await db.EnergySystems.CountAsync());
        // 园区根节点只建一个。
        Assert.Equal(1, await db.EnergyOrgUnits.CountAsync(unit => unit.Level == EnergyOrgUnit.ParkLevel));
    }

    [Fact]
    public async Task StartAsync_Should_Not_Override_User_Changes()
    {
        using EnergySqliteTestDatabase database = new();
        await CreateInitializer(database).StartAsync(CancellationToken.None);

        await using (DataAcquisitionPlatformDbContext db = database.CreateContext())
        {
            // 现场把「电」改成「电力」、把「照明」系统停用。
            EnergyMedium elec = await db.EnergyMedia.SingleAsync(item => item.Code == "ELEC");
            elec.Name = "电力";
            EnergySystemType lighting = await db.EnergySystems.SingleAsync(item => item.Code == "LIGHTING");
            lighting.IsEnabled = false;
            await db.SaveChangesAsync();
        }

        await CreateInitializer(database).StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db2 = database.CreateContext();
        // 「只补不覆盖」：名称与启停状态都是用户的决定，重启不能盖回去。
        Assert.Equal("电力", await db2.EnergyMedia.Where(item => item.Code == "ELEC").Select(item => item.Name).SingleAsync());
        EnergySystemType lightingAfter = await db2.EnergySystems.SingleAsync(item => item.Code == "LIGHTING");
        Assert.False(lightingAfter.IsEnabled);
        Assert.Equal(5, await db2.EnergyMedia.CountAsync());
        Assert.Equal(5, await db2.EnergySystems.CountAsync());
    }

    [Fact]
    public async Task StartAsync_Should_Only_Fill_Missing_Items()
    {
        using EnergySqliteTestDatabase database = new();
        await CreateInitializer(database).StartAsync(CancellationToken.None);

        Guid parkId;
        await using (DataAcquisitionPlatformDbContext db = database.CreateContext())
        {
            EnergyOrgUnit park = await db.EnergyOrgUnits.SingleAsync(unit => unit.Level == EnergyOrgUnit.ParkLevel);
            parkId = park.Id;

            // 删掉水介质、照明系统与园区根节点，同时保留用户改名过的电介质。
            EnergyMedium elec = await db.EnergyMedia.SingleAsync(item => item.Code == "ELEC");
            elec.Name = "电力（改）";
            db.EnergyMedia.Remove(await db.EnergyMedia.SingleAsync(item => item.Code == "WATER"));
            db.EnergySystems.Remove(await db.EnergySystems.SingleAsync(item => item.Code == "LIGHTING"));
            db.EnergyOrgUnits.Remove(park);
            await db.SaveChangesAsync();
        }

        await CreateInitializer(database).StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db2 = database.CreateContext();
        // 缺的按默认值补回，用户改过的不动。
        EnergyMedium water = await db2.EnergyMedia.SingleAsync(item => item.Code == "WATER");
        Assert.Equal("水", water.Name);
        Assert.Equal(0.2571m, water.CoalFactor);
        Assert.Equal("电力（改）", await db2.EnergyMedia.Where(item => item.Code == "ELEC").Select(item => item.Name).SingleAsync());
        Assert.True(await db2.EnergySystems.AnyAsync(item => item.Code == "LIGHTING" && item.IsEnabled));

        EnergyOrgUnit newPark = await db2.EnergyOrgUnits.SingleAsync(unit => unit.Level == EnergyOrgUnit.ParkLevel);
        Assert.NotEqual(parkId, newPark.Id);
        Assert.Equal(5, await db2.EnergyMedia.CountAsync());
        Assert.Equal(5, await db2.EnergySystems.CountAsync());
    }

    [Fact]
    public async Task StartAsync_Code_Match_Should_Be_Case_Insensitive()
    {
        using EnergySqliteTestDatabase database = new();

        // 已有一条小写编码的介质：内置 ELEC 不应该再补一份大写的。
        await using (DataAcquisitionPlatformDbContext db = database.CreateContext())
        {
            db.EnergyMedia.Add(new EnergyMedium
            {
                Id = Guid.NewGuid(),
                Code = "elec",
                Name = "自定义电",
                Unit = "kWh",
                IsPrimaryEnergy = true,
                CoalFactor = null,
                SortOrder = 10,
                IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await CreateInitializer(database).StartAsync(CancellationToken.None);

        await using DataAcquisitionPlatformDbContext db2 = database.CreateContext();
        List<string> codes = await db2.EnergyMedia.Select(item => item.Code).ToListAsync();
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(codes, code => code == "ELEC");
        Assert.Equal("自定义电", await db2.EnergyMedia.Where(item => item.Code == "elec").Select(item => item.Name).SingleAsync());
        // 其余介质照常补齐。
        Assert.True(await db2.EnergyMedia.AnyAsync(item => item.Code == "WATER"));
    }

    [Fact]
    public async Task StartAsync_WhenScopeFails_Should_Swallow_Exception()
    {
        // 字典是附属模块：初始化失败不能把整个站点拖住。
        EnergyDictionaryInitializer initializer =
            new(new ThrowingScopeFactory(), NullLogger<EnergyDictionaryInitializer>.Instance);

        Exception? thrown = await Record.ExceptionAsync(() => initializer.StartAsync(CancellationToken.None));

        Assert.Null(thrown);
    }

    [Fact]
    public async Task StopAsync_Should_Complete_Immediately()
    {
        using EnergySqliteTestDatabase database = new();
        EnergyDictionaryInitializer initializer = CreateInitializer(database);

        await initializer.StopAsync(CancellationToken.None);
    }

    /// <summary>作用域工厂一创建作用域就抛异常，用于验证异常不外抛。</summary>
    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            throw new InvalidOperationException("测试替身：作用域工厂不可用。");
        }
    }
}
