using DAP.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAP.Infrastructure.DataAccess.Persistence;

/// <summary>
/// 表示数据采集平台的 PostgreSQL 上下文。
/// </summary>
public sealed class DataAcquisitionPlatformDbContext : DbContext
{
    /// <summary>
    /// 初始化一个新的 <see cref="DataAcquisitionPlatformDbContext"/> 实例。
    /// </summary>
    /// <param name="options">上下文配置。</param>
    public DataAcquisitionPlatformDbContext(DbContextOptions<DataAcquisitionPlatformDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// 获取采集点集合。
    /// </summary>
    public DbSet<CollectionPoint> CollectionPoints => Set<CollectionPoint>();

    /// <summary>
    /// 获取采集数据记录集合。
    /// </summary>
    public DbSet<CollectionDataRecord> CollectionDataRecords => Set<CollectionDataRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DataAcquisitionPlatformDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
