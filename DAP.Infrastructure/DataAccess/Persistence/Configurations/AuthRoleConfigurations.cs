using DAP.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DAP.Infrastructure.DataAccess.Persistence.Configurations;

/// <summary>
/// 配置角色实体映射。
/// </summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable(
            "roles",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_roles_code_not_blank", "btrim(code) <> ''");
                tableBuilder.HasCheckConstraint("ck_roles_name_not_blank", "btrim(name) <> ''");
                tableBuilder.HasCheckConstraint("ck_roles_page_level", "page_level >= 1");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        builder.Property(item => item.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(item => item.PageLevel).HasColumnName("page_level").IsRequired();
        builder.Property(item => item.IsBuiltin).HasColumnName("is_builtin").IsRequired();
        builder.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(item => item.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasDatabaseName("ux_roles_code");
    }
}

/// <summary>
/// 配置权限点实体映射。
/// </summary>
public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable(
            "permissions",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_permissions_code_not_blank", "btrim(code) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_permissions_code_prefix",
                    "code LIKE 'action.%'");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Code).HasColumnName("code").HasMaxLength(100).IsRequired();
        builder.Property(item => item.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(item => item.Group).HasColumnName("group_name").HasMaxLength(100);
        builder.Property(item => item.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasDatabaseName("ux_permissions_code");
    }
}

/// <summary>
/// 配置角色与权限点关联实体映射。
/// </summary>
public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");

        builder.HasKey(item => new {item.RoleId, item.PermissionId});
        builder.Property(item => item.RoleId).HasColumnName("role_id");
        builder.Property(item => item.PermissionId).HasColumnName("permission_id");

        builder.HasOne(item => item.Role)
            .WithMany(role => role.RolePermissions)
            .HasForeignKey(item => item.RoleId)
            .HasConstraintName("fk_role_permissions_roles")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.Permission)
            .WithMany()
            .HasForeignKey(item => item.PermissionId)
            .HasConstraintName("fk_role_permissions_permissions")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// 配置用户与角色关联实体映射。
/// </summary>
public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");

        builder.HasKey(item => new {item.UserId, item.RoleId});
        builder.Property(item => item.UserId).HasColumnName("user_id");
        builder.Property(item => item.RoleId).HasColumnName("role_id");

        builder.HasOne(item => item.User)
            .WithMany(user => user.UserRoles)
            .HasForeignKey(item => item.UserId)
            .HasConstraintName("fk_user_roles_app_users")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.Role)
            .WithMany()
            .HasForeignKey(item => item.RoleId)
            .HasConstraintName("fk_user_roles_roles")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// 配置审计日志实体映射。
/// </summary>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable(
            "audit_logs",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_audit_logs_action_code_not_blank", "btrim(action_code) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_audit_logs_result",
                    "result IN ('success', 'failed')");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(item => item.UserId).HasColumnName("user_id");
        builder.Property(item => item.UserName).HasColumnName("user_name").HasMaxLength(100);
        builder.Property(item => item.DepartmentName).HasColumnName("department_name").HasMaxLength(200);
        builder.Property(item => item.ActionCode).HasColumnName("action_code").HasMaxLength(100).IsRequired();
        builder.Property(item => item.TargetType).HasColumnName("target_type").HasMaxLength(100);
        builder.Property(item => item.TargetId).HasColumnName("target_id").HasMaxLength(100);
        builder.Property(item => item.TargetName).HasColumnName("target_name").HasMaxLength(200);
        builder.Property(item => item.Result).HasColumnName("result").HasMaxLength(20).IsRequired();
        builder.Property(item => item.Detail).HasColumnName("detail").HasColumnType("jsonb");
        builder.Property(item => item.Ip).HasColumnName("ip").HasMaxLength(64);
        builder.Property(item => item.UserAgent).HasColumnName("user_agent").HasMaxLength(500);

        builder.HasIndex(item => item.OccurredAt)
            .IsDescending()
            .HasDatabaseName("ix_audit_logs_occurred_at");

        builder.HasIndex(item => new {item.UserId, item.OccurredAt})
            .IsDescending(false, true)
            .HasDatabaseName("ix_audit_logs_user_occurred_at");

        builder.HasIndex(item => new {item.TargetType, item.TargetId})
            .HasDatabaseName("ix_audit_logs_target");
    }
}
