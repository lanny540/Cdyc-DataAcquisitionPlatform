using DAP.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DAP.Infrastructure.DataAccess.Persistence.Configurations;

/// <summary>
/// 配置能源介质实体映射。
/// </summary>
public sealed class EnergyMediumConfiguration : IEntityTypeConfiguration<EnergyMedium>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EnergyMedium> builder)
    {
        builder.ToTable(
            "energy_media",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_energy_media_code_not_blank", "btrim(code) <> ''");
                tableBuilder.HasCheckConstraint("ck_energy_media_name_not_blank", "btrim(name) <> ''");
                tableBuilder.HasCheckConstraint("ck_energy_media_unit_not_blank", "btrim(unit) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_energy_media_coal_factor",
                    "coal_factor is null or coal_factor >= 0");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Code).HasColumnName("code").HasMaxLength(32).IsRequired();
        builder.Property(item => item.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        builder.Property(item => item.Unit).HasColumnName("unit").HasMaxLength(16).IsRequired();
        builder.Property(item => item.IsPrimaryEnergy).HasColumnName("is_primary_energy").IsRequired();
        builder.Property(item => item.CoalFactor).HasColumnName("coal_factor").HasPrecision(18, 6);
        builder.Property(item => item.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasDatabaseName("ux_energy_media_code");
    }
}

/// <summary>
/// 配置用能用途实体映射。
/// </summary>
public sealed class EnergyUsageTypeConfiguration : IEntityTypeConfiguration<EnergyUsageType>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EnergyUsageType> builder)
    {
        builder.ToTable(
            "energy_usage_types",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_energy_usage_types_code_not_blank", "btrim(code) <> ''");
                tableBuilder.HasCheckConstraint("ck_energy_usage_types_name_not_blank", "btrim(name) <> ''");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Code).HasColumnName("code").HasMaxLength(32).IsRequired();
        builder.Property(item => item.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        builder.Property(item => item.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(item => item.IsSystem).HasColumnName("is_system").IsRequired();
        builder.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasDatabaseName("ux_energy_usage_types_code");
    }
}

/// <summary>
/// 配置设备系统实体映射。
/// </summary>
public sealed class EnergySystemTypeConfiguration : IEntityTypeConfiguration<EnergySystemType>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EnergySystemType> builder)
    {
        builder.ToTable(
            "energy_systems",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_energy_systems_code_not_blank", "btrim(code) <> ''");
                tableBuilder.HasCheckConstraint("ck_energy_systems_name_not_blank", "btrim(name) <> ''");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Code).HasColumnName("code").HasMaxLength(32).IsRequired();
        builder.Property(item => item.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        builder.Property(item => item.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(item => item.IsSystem).HasColumnName("is_system").IsRequired();
        builder.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasDatabaseName("ux_energy_systems_code");
    }
}

/// <summary>
/// 配置能源组织节点实体映射。
/// </summary>
public sealed class EnergyOrgUnitConfiguration : IEntityTypeConfiguration<EnergyOrgUnit>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EnergyOrgUnit> builder)
    {
        builder.ToTable(
            "energy_org_units",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_energy_org_units_code_not_blank", "btrim(code) <> ''");
                tableBuilder.HasCheckConstraint("ck_energy_org_units_name_not_blank", "btrim(name) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_energy_org_units_level",
                    "level in ('park', 'department', 'process')");

                // 层级与父子关系必须互洽：园区是根，其余两级必须有上级。
                // 少了这条约束，一棵挂着孤儿节点的树会让上卷汇总悄悄漏数。
                tableBuilder.HasCheckConstraint(
                    "ck_energy_org_units_parent_by_level",
                    "(level = 'park' and parent_id is null) or (level <> 'park' and parent_id is not null)");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        builder.Property(item => item.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(item => item.Level).HasColumnName("level").HasMaxLength(16).IsRequired();
        builder.Property(item => item.ParentId).HasColumnName("parent_id");
        builder.Property(item => item.DepartmentId).HasColumnName("department_id");
        builder.Property(item => item.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasDatabaseName("ux_energy_org_units_code");

        builder.HasIndex(item => item.ParentId)
            .HasDatabaseName("ix_energy_org_units_parent_id");

        builder.HasIndex(item => item.DepartmentId)
            .HasDatabaseName("ix_energy_org_units_department_id");

        builder.HasOne(item => item.Parent)
            .WithMany(parent => parent.Children)
            .HasForeignKey(item => item.ParentId)
            .HasConstraintName("fk_energy_org_units_parent")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Department>()
            .WithMany()
            .HasForeignKey(item => item.DepartmentId)
            .HasConstraintName("fk_energy_org_units_departments")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// 配置能源计量点实体映射。
/// </summary>
public sealed class EnergyMeterConfiguration : IEntityTypeConfiguration<EnergyMeter>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EnergyMeter> builder)
    {
        builder.ToTable(
            "energy_meters",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_energy_meters_code_not_blank", "btrim(code) <> ''");
                tableBuilder.HasCheckConstraint("ck_energy_meters_name_not_blank", "btrim(name) <> ''");
                tableBuilder.HasCheckConstraint("ck_energy_meters_ratio_positive", "ratio > 0");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        builder.Property(item => item.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(item => item.MediumId).HasColumnName("medium_id");
        builder.Property(item => item.OrgUnitId).HasColumnName("org_unit_id");
        builder.Property(item => item.UsageTypeId).HasColumnName("usage_type_id");
        builder.Property(item => item.SystemTypeId).HasColumnName("system_type_id");
        builder.Property(item => item.ParentMeterId).HasColumnName("parent_meter_id");
        builder.Property(item => item.IsSummary).HasColumnName("is_summary").IsRequired();
        builder.Property(item => item.Ratio).HasColumnName("ratio").HasPrecision(18, 6).IsRequired();
        builder.Property(item => item.IsCumulative).HasColumnName("is_cumulative").IsRequired();
        builder.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(item => item.Remark).HasColumnName("remark").HasMaxLength(500);
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasDatabaseName("ux_energy_meters_code");

        builder.HasIndex(item => item.MediumId).HasDatabaseName("ix_energy_meters_medium_id");
        builder.HasIndex(item => item.OrgUnitId).HasDatabaseName("ix_energy_meters_org_unit_id");
        builder.HasIndex(item => item.UsageTypeId).HasDatabaseName("ix_energy_meters_usage_type_id");
        builder.HasIndex(item => item.SystemTypeId).HasDatabaseName("ix_energy_meters_system_type_id");
        builder.HasIndex(item => item.ParentMeterId).HasDatabaseName("ix_energy_meters_parent_meter_id");

        // 字典项一律 Restrict：删掉一个用途不该把挂着它的计量点一起删掉，
        // 那等于悄悄抹掉历史账。
        builder.HasOne(item => item.Medium)
            .WithMany(medium => medium.Meters)
            .HasForeignKey(item => item.MediumId)
            .HasConstraintName("fk_energy_meters_energy_media")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(item => item.UsageType)
            .WithMany(usage => usage.Meters)
            .HasForeignKey(item => item.UsageTypeId)
            .HasConstraintName("fk_energy_meters_energy_usage_types")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(item => item.SystemType)
            .WithMany(system => system.Meters)
            .HasForeignKey(item => item.SystemTypeId)
            .HasConstraintName("fk_energy_meters_energy_systems")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(item => item.OrgUnit)
            .WithMany(org => org.Meters)
            .HasForeignKey(item => item.OrgUnitId)
            .HasConstraintName("fk_energy_meters_energy_org_units")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(item => item.ParentMeter)
            .WithMany(parent => parent.ChildMeters)
            .HasForeignKey(item => item.ParentMeterId)
            .HasConstraintName("fk_energy_meters_parent")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// 配置能源读数实体映射。
/// </summary>
public sealed class EnergyReadingConfiguration : IEntityTypeConfiguration<EnergyReading>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EnergyReading> builder)
    {
        builder.ToTable(
            "energy_readings",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_energy_readings_source",
                    "source in ('manual', 'collection', 'import')");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.MeterId).HasColumnName("meter_id");
        builder.Property(item => item.ReadAt).HasColumnName("read_at").IsRequired();
        builder.Property(item => item.Value).HasColumnName("value").HasPrecision(18, 6).IsRequired();
        builder.Property(item => item.Source).HasColumnName("source").HasMaxLength(16).IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();

        // 同一块表在同一时刻只允许一条读数：重复写入会让差分量凭空翻倍，
        // 而且这种错误在报表上看起来完全正常，只是数值偏大。
        builder.HasIndex(item => new { item.MeterId, item.ReadAt })
            .IsUnique()
            .HasDatabaseName("ux_energy_readings_meter_read_at");

        builder.HasOne(item => item.Meter)
            .WithMany(meter => meter.Readings)
            .HasForeignKey(item => item.MeterId)
            .HasConstraintName("fk_energy_readings_energy_meters")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
