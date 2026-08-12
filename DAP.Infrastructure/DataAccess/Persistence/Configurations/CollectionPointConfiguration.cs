using DAP.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DAP.Infrastructure.DataAccess.Persistence.Configurations;

/// <summary>
/// 配置采集点实体映射。
/// </summary>
public sealed class CollectionPointConfiguration : IEntityTypeConfiguration<CollectionPoint>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CollectionPoint> builder)
    {
        builder.ToTable(
            "collection_points",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_collection_points_code_not_blank", "btrim(code) <> ''");
                tableBuilder.HasCheckConstraint("ck_collection_points_code_upper", "code = upper(code)");
                tableBuilder.HasCheckConstraint("ck_collection_points_name_not_blank", "btrim(name) <> ''");
                tableBuilder.HasCheckConstraint("ck_collection_points_protocol_not_blank", "btrim(protocol) <> ''");
                tableBuilder.HasCheckConstraint("ck_collection_points_endpoint_not_blank", "btrim(endpoint) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_collection_points_communication_status",
                    "communication_status IN ('在线', '离线', '停用', '未知')");
                tableBuilder.HasCheckConstraint(
                    "ck_collection_points_source",
                    "source IN ('Server', 'Local')");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        builder.Property(item => item.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(item => item.Protocol).HasColumnName("protocol").HasMaxLength(100).IsRequired();
        builder.Property(item => item.Endpoint).HasColumnName("endpoint").HasMaxLength(500).IsRequired();
        builder.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(item => item.CommunicationStatus).HasColumnName("communication_status").HasMaxLength(50)
            .IsRequired();
        builder.Property(item => item.Source).HasColumnName("source").HasMaxLength(50).IsRequired();
        builder.Property(item => item.LastError).HasColumnName("last_error");
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasDatabaseName("ux_collection_points_code");

        builder.HasIndex(item => new {item.CommunicationStatus, item.UpdatedAt})
            .HasDatabaseName("ix_collection_points_status_updated_at");

        builder.HasIndex(item => new {item.Source, item.UpdatedAt})
            .HasDatabaseName("ix_collection_points_source_updated_at");
    }
}
