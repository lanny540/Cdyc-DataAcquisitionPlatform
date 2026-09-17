using DAP.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DAP.Infrastructure.DataAccess.Persistence.Configurations;

/// <summary>
/// 配置后台数据定义实体映射。
/// </summary>
public sealed class ManagedDataDefinitionConfiguration : IEntityTypeConfiguration<ManagedDataDefinition>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ManagedDataDefinition> builder)
    {
        builder.ToTable(
            "managed_data_definitions",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_managed_data_definitions_code_not_blank", "btrim(code) <> ''");
                tableBuilder.HasCheckConstraint("ck_managed_data_definitions_code_upper", "code = upper(code)");
                tableBuilder.HasCheckConstraint("ck_managed_data_definitions_name_not_blank", "btrim(name) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_managed_data_definitions_acquisition_type_not_blank",
                    "btrim(acquisition_type) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_managed_data_definitions_connection_address_not_blank",
                    "btrim(connection_address) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_managed_data_definitions_identifier_not_blank",
                    "btrim(identifier) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_managed_data_definitions_department_not_blank",
                    "btrim(department) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_managed_data_definitions_process_code_not_blank",
                    "btrim(process_code) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_managed_data_definitions_data_category_not_blank",
                    "btrim(data_category) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_managed_data_definitions_interval_seconds_positive",
                    "collection_interval_seconds > 0");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        builder.Property(item => item.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(item => item.AcquisitionType).HasColumnName("acquisition_type").HasMaxLength(100).IsRequired();
        builder.Property(item => item.ConnectionAddress).HasColumnName("connection_address").HasMaxLength(500)
            .IsRequired();
        builder.Property(item => item.ServerConnectionId).HasColumnName("server_connection_id");
        builder.Property(item => item.Identifier).HasColumnName("identifier").HasMaxLength(200).IsRequired();
        builder.Property(item => item.Department).HasColumnName("department").HasMaxLength(100).IsRequired();
        builder.Property(item => item.ProcessCode).HasColumnName("process_code").HasMaxLength(100).IsRequired();
        builder.Property(item => item.DataCategory).HasColumnName("data_category").HasMaxLength(100).IsRequired();
        builder.Property(item => item.Unit).HasColumnName("unit").HasMaxLength(50).HasDefaultValue(string.Empty)
            .IsRequired();
        builder.Property(item => item.Description).HasColumnName("description").HasMaxLength(1000)
            .HasDefaultValue(string.Empty).IsRequired();
        builder.Property(item => item.ConfigurationJson).HasColumnName("configuration_json").HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(item => item.BusinessTagsJson).HasColumnName("business_tags_json").HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(item => item.CollectionIntervalSeconds).HasColumnName("collection_interval_seconds")
            .HasDefaultValue(300).IsRequired();
        builder.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasDatabaseName("ux_managed_data_definitions_code");

        builder.HasIndex(item => new {item.AcquisitionType, item.IsEnabled, item.UpdatedAt})
            .HasDatabaseName("ix_managed_data_definitions_type_enabled_updated_at");

        builder.HasIndex(item => new {item.Department, item.ProcessCode})
            .HasDatabaseName("ix_managed_data_definitions_department_process_code");

        builder.HasIndex(item => item.ServerConnectionId)
            .HasDatabaseName("ix_managed_data_definitions_server_connection_id");

        builder.HasOne(item => item.ServerConnection)
            .WithMany(connection => connection.DataDefinitions)
            .HasForeignKey(item => item.ServerConnectionId)
            .HasConstraintName("managed_data_definitions_server_connection_id_fkey")
            .OnDelete(DeleteBehavior.SetNull);
    }
}
