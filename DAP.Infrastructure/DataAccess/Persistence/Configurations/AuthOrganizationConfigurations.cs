using DAP.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DAP.Infrastructure.DataAccess.Persistence.Configurations;

/// <summary>
/// 配置部门实体映射。
/// </summary>
public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable(
            "departments",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_departments_code_not_blank", "btrim(code) <> ''");
                tableBuilder.HasCheckConstraint("ck_departments_name_not_blank", "btrim(name) <> ''");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        builder.Property(item => item.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(item => item.ExternalCode).HasColumnName("external_code").HasMaxLength(200);
        builder.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasDatabaseName("ux_departments_code");

        builder.HasIndex(item => item.ExternalCode)
            .IsUnique()
            .HasDatabaseName("ux_departments_external_code")
            .HasFilter("external_code IS NOT NULL");
    }
}

/// <summary>
/// 配置平台账号实体映射。
/// </summary>
public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable(
            "app_users",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_app_users_user_name_not_blank", "btrim(user_name) <> ''");
                tableBuilder.HasCheckConstraint("ck_app_users_user_name_lower", "user_name = lower(user_name)");
                tableBuilder.HasCheckConstraint("ck_app_users_display_name_not_blank", "btrim(display_name) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_app_users_access_failed_count",
                    "access_failed_count >= 0");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.UserName).HasColumnName("user_name").HasMaxLength(100).IsRequired();
        builder.Property(item => item.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
        builder.Property(item => item.DepartmentId).HasColumnName("department_id");
        builder.Property(item => item.Email).HasColumnName("email").HasMaxLength(200);
        builder.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(item => item.MustChangePassword).HasColumnName("must_change_password").IsRequired();
        builder.Property(item => item.PasswordHash).HasColumnName("password_hash").HasMaxLength(500);
        builder.Property(item => item.AccessFailedCount).HasColumnName("access_failed_count").IsRequired();
        builder.Property(item => item.LockoutEndAt).HasColumnName("lockout_end_at");
        builder.Property(item => item.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => item.UserName)
            .IsUnique()
            .HasDatabaseName("ux_app_users_user_name");

        builder.HasIndex(item => item.DepartmentId)
            .HasDatabaseName("ix_app_users_department_id");

        builder.HasOne(item => item.Department)
            .WithMany(department => department.Users)
            .HasForeignKey(item => item.DepartmentId)
            .HasConstraintName("fk_app_users_departments")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// 配置外部身份绑定实体映射。
/// </summary>
public sealed class ExternalIdentityConfiguration : IEntityTypeConfiguration<ExternalIdentity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ExternalIdentity> builder)
    {
        builder.ToTable("external_identities");

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.UserId).HasColumnName("user_id");
        builder.Property(item => item.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
        builder.Property(item => item.ExternalId).HasColumnName("external_id").HasMaxLength(200).IsRequired();
        builder.Property(item => item.EmployeeNo).HasColumnName("employee_no").HasMaxLength(64);
        builder.Property(item => item.LastSyncedAt).HasColumnName("last_synced_at");
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => new {item.Provider, item.ExternalId})
            .IsUnique()
            .HasDatabaseName("ux_external_identities_provider_external_id");

        builder.HasIndex(item => item.EmployeeNo)
            .HasDatabaseName("ix_external_identities_employee_no");

        builder.HasOne<AppUser>()
            .WithMany(user => user.ExternalIdentities)
            .HasForeignKey(item => item.UserId)
            .HasConstraintName("fk_external_identities_app_users")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
