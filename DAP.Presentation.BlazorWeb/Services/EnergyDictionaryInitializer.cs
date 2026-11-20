using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Catalog;
using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 能源字典初始化器：把内置的介质、用能用途与设备系统补齐到数据库。
///
/// **只补不覆盖**：已存在的编码一律不动，包括用户改过的名称与启停状态。
/// 内置字典是「默认值」而不是「权威值」——现场把「管理」改成「管理办公」之后，
/// 每次重启又改回去，是最容易让人放弃维护配置的那类行为。
///
/// **失败不阻断启动**：与认证初始化器不同，能源字典缺失只会让能源板块没有分类可选，
/// 不影响登录与采集。这里吞掉异常并记日志，避免一个附属模块把整个站点拖住。
/// </summary>
public sealed class EnergyDictionaryInitializer(
    IServiceScopeFactory scopeFactory,
    ILogger<EnergyDictionaryInitializer> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            DataAcquisitionPlatformDbContext db =
                scope.ServiceProvider.GetRequiredService<DataAcquisitionPlatformDbContext>();

            DateTimeOffset now = DateTimeOffset.UtcNow;
            int added = 0;

            added += await EnsureMediaAsync(db, now, cancellationToken);
            added += await EnsureUsageTypesAsync(db, now, cancellationToken);
            added += await EnsureSystemsAsync(db, now, cancellationToken);
            added += await EnsureParkAsync(db, now, cancellationToken);

            if (added > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("能源字典已补齐 {Count} 项内置数据。", added);
            }
        }
        catch (Exception ex)
        {
            // 不阻断启动：能源板块没起来，其他模块照常可用。
            logger.LogError(ex, "能源字典初始化失败，能源板块可能缺少可选分类。");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static async Task<int> EnsureMediaAsync(
        DataAcquisitionPlatformDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        HashSet<string> existing = await db.EnergyMedia
            .Select(item => item.Code)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);

        int added = 0;

        foreach (EnergyMediumSeed seed in EnergyDictionaryCatalog.Media)
        {
            if (existing.Contains(seed.Code))
            {
                continue;
            }

            db.EnergyMedia.Add(new EnergyMedium
            {
                Id = Guid.NewGuid(),
                Code = seed.Code,
                Name = seed.Name,
                Unit = seed.Unit,
                IsPrimaryEnergy = seed.IsPrimaryEnergy,
                CoalFactor = seed.CoalFactor,
                SortOrder = seed.SortOrder,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            });

            added++;
        }

        return added;
    }

    private static async Task<int> EnsureUsageTypesAsync(
        DataAcquisitionPlatformDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        HashSet<string> existing = await db.EnergyUsageTypes
            .Select(item => item.Code)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);

        int added = 0;

        foreach (EnergyCategorySeed seed in EnergyDictionaryCatalog.UsageTypes)
        {
            if (existing.Contains(seed.Code))
            {
                continue;
            }

            db.EnergyUsageTypes.Add(new EnergyUsageType
            {
                Id = Guid.NewGuid(),
                Code = seed.Code,
                Name = seed.Name,
                SortOrder = seed.SortOrder,
                IsSystem = true,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            });

            added++;
        }

        return added;
    }

    private static async Task<int> EnsureSystemsAsync(
        DataAcquisitionPlatformDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        HashSet<string> existing = await db.EnergySystems
            .Select(item => item.Code)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);

        int added = 0;

        foreach (EnergyCategorySeed seed in EnergyDictionaryCatalog.Systems)
        {
            if (existing.Contains(seed.Code))
            {
                continue;
            }

            db.EnergySystems.Add(new EnergySystemType
            {
                Id = Guid.NewGuid(),
                Code = seed.Code,
                Name = seed.Name,
                SortOrder = seed.SortOrder,
                IsSystem = true,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            });

            added++;
        }

        return added;
    }

    /// <summary>
    /// 保证至少有一个园区根节点。没有它，任何计量点都挂不上去。
    /// </summary>
    private static async Task<int> EnsureParkAsync(
        DataAcquisitionPlatformDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        bool hasRoot = await db.EnergyOrgUnits
            .AnyAsync(item => item.Level == EnergyOrgUnit.ParkLevel, cancellationToken);

        if (hasRoot)
        {
            return 0;
        }

        db.EnergyOrgUnits.Add(new EnergyOrgUnit
        {
            Id = Guid.NewGuid(),
            Code = "PARK",
            Name = "园区",
            Level = EnergyOrgUnit.ParkLevel,
            ParentId = null,
            SortOrder = 10,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        });

        return 1;
    }
}
