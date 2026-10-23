using DAP.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DAP.Infrastructure.DataAccess.Persistence.Configurations;

/// <summary>
/// 配置外部服务器实体映射。
/// </summary>
public sealed class ServerConnectionConfiguration : IEntityTypeConfiguration<ServerConnection>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ServerConnection> builder)
    {
        builder.ToTable(
            "server_connections",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_server_connections_code_not_blank", "btrim(code) <> ''");
                tableBuilder.HasCheckConstraint("ck_server_connections_code_upper", "code = upper(code)");
                tableBuilder.HasCheckConstraint("ck_server_connections_display_name_not_blank",
                    "btrim(display_name) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_server_connections_acquisition_type_not_blank",
                    "btrim(acquisition_type) <> ''");
                tableBuilder.HasCheckConstraint("ck_server_connections_address_not_blank", "btrim(address) <> ''");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        builder.Property(item => item.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        builder.Property(item => item.AcquisitionType).HasColumnName("acquisition_type").HasMaxLength(100).IsRequired();
        builder.Property(item => item.Address).HasColumnName("address").HasMaxLength(500).IsRequired();
        builder.Property(item => item.Description).HasColumnName("description").HasMaxLength(1000)
            .HasDefaultValue(string.Empty).IsRequired();
        builder.Property(item => item.ConfigurationJson).HasColumnName("configuration_json").HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(item => item.LastProbeStatus).HasColumnName("last_probe_status").HasMaxLength(50)
            .HasDefaultValue("未测试").IsRequired();
        builder.Property(item => item.LastProbeMessage).HasColumnName("last_probe_message").HasMaxLength(1000);
        builder.Property(item => item.LastProbedAt).HasColumnName("last_probed_at");
        builder.Property(item => item.CreatedAt).HasColumnName("created_at")
            .HasDefaultValueSql("now()").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasDatabaseName("ux_server_connections_code");

        builder.HasIndex(item => new {item.AcquisitionType, item.IsEnabled, item.UpdatedAt})
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_server_connections_type_enabled_updated_at");

        builder.HasIndex(item => new {item.AcquisitionType, item.Address})
            .IsUnique()
            .HasDatabaseName("ux_server_connections_type_address");
    }
}
