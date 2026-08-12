using DAP.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DAP.Infrastructure.DataAccess.Persistence.Configurations;

/// <summary>
/// 配置采集数据记录实体映射。
/// </summary>
public sealed class CollectionDataRecordConfiguration : IEntityTypeConfiguration<CollectionDataRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CollectionDataRecord> builder)
    {
        builder.ToTable(
            "collection_data_records",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_collection_data_records_metric_name_not_blank", "btrim(metric_name) <> ''");
                tableBuilder.HasCheckConstraint("ck_collection_data_records_unit_not_blank", "btrim(unit) <> ''");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.CollectionPointId).HasColumnName("collection_point_id").IsRequired();
        builder.Property(item => item.MetricName).HasColumnName("metric_name").HasMaxLength(100).IsRequired();
        builder.Property(item => item.Value).HasColumnName("value").HasPrecision(18, 4).IsRequired();
        builder.Property(item => item.Unit).HasColumnName("unit").HasMaxLength(50).IsRequired();
        builder.Property(item => item.CollectedAt).HasColumnName("collected_at").IsRequired();

        builder.HasOne(item => item.CollectionPoint)
            .WithMany(point => point.Records)
            .HasForeignKey(item => item.CollectionPointId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(item => item.CollectedAt)
            .HasDatabaseName("ix_collection_data_records_collected_at");

        builder.HasIndex(item => item.CollectionPointId)
            .HasDatabaseName("ix_collection_data_records_collection_point_id");

        builder.HasIndex(item => new { item.CollectionPointId, item.MetricName, item.CollectedAt })
            .HasDatabaseName("ix_collection_data_records_point_metric_collected_at");
    }
}
