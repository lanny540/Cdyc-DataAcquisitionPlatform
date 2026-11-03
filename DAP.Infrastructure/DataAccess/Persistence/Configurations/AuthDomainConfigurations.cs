using DAP.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DAP.Infrastructure.DataAccess.Persistence.Configurations;

/// <summary>
/// 配置职能域实体映射。
/// </summary>
public sealed class BusinessDomainConfiguration : IEntityTypeConfiguration<BusinessDomain>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BusinessDomain> builder)
    {
        builder.ToTable(
            "business_domains",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_business_domains_code_not_blank", "btrim(code) <> ''");
                tableBuilder.HasCheckConstraint("ck_business_domains_name_not_blank", "btrim(name) <> ''");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        builder.Property(item => item.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(item => item.Icon).HasColumnName("icon").HasMaxLength(100);
        builder.Property(item => item.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(item => item.IsSystem).HasColumnName("is_system").IsRequired();
        builder.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(item => item.VisibleWhenEmpty).HasColumnName("visible_when_empty").IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasDatabaseName("ux_business_domains_code");

        builder.HasIndex(item => item.SortOrder)
            .HasDatabaseName("ix_business_domains_sort_order");
    }
}

/// <summary>
/// 配置域内页面实体映射。
/// </summary>
public sealed class DomainPageConfiguration : IEntityTypeConfiguration<DomainPage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DomainPage> builder)
    {
        builder.ToTable(
            "domain_pages",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_domain_pages_page_key_not_blank", "btrim(page_key) <> ''");
                tableBuilder.HasCheckConstraint("ck_domain_pages_route_not_blank", "btrim(route) <> ''");
                tableBuilder.HasCheckConstraint("ck_domain_pages_title_not_blank", "btrim(title) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_domain_pages_access_level",
                    "access_level IN (1, 2)");
                tableBuilder.HasCheckConstraint(
                    "ck_domain_pages_source",
                    "source IN ('code', 'override')");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.PageKey).HasColumnName("page_key").HasMaxLength(100).IsRequired();
        builder.Property(item => item.DomainId).HasColumnName("domain_id");
        builder.Property(item => item.Route).HasColumnName("route").HasMaxLength(200).IsRequired();
        builder.Property(item => item.Title).HasColumnName("title").HasMaxLength(100).IsRequired();
        builder.Property(item => item.GroupTitle).HasColumnName("group_title").HasMaxLength(100);
        builder.Property(item => item.Icon).HasColumnName("icon").HasMaxLength(100);
        builder.Property(item => item.Accent).HasColumnName("accent").HasMaxLength(100);
        builder.Property(item => item.AccessLevel).HasColumnName("access_level").IsRequired();
        builder.Property(item => item.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(item => item.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
        builder.Property(item => item.IsOrphaned).HasColumnName("is_orphaned").IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => item.PageKey)
            .IsUnique()
            .HasDatabaseName("ux_domain_pages_page_key");

        builder.HasIndex(item => item.Route)
            .HasDatabaseName("ix_domain_pages_route");

        builder.HasIndex(item => new {item.DomainId, item.SortOrder})
            .HasDatabaseName("ix_domain_pages_domain_sort_order");

        builder.HasOne(item => item.Domain)
            .WithMany(domain => domain.Pages)
            .HasForeignKey(item => item.DomainId)
            .HasConstraintName("fk_domain_pages_business_domains")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// 配置「部门 × 职能域」授权实体映射。
/// </summary>
public sealed class DepartmentDomainGrantConfiguration : IEntityTypeConfiguration<DepartmentDomainGrant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DepartmentDomainGrant> builder)
    {
        builder.ToTable("department_domain_grants");

        builder.HasKey(item => new {item.DepartmentId, item.DomainId});
        builder.Property(item => item.DepartmentId).HasColumnName("department_id");
        builder.Property(item => item.DomainId).HasColumnName("domain_id");
        builder.Property(item => item.GrantedAt).HasColumnName("granted_at").IsRequired();
        builder.Property(item => item.GrantedBy).HasColumnName("granted_by");

        builder.HasOne(item => item.Department)
            .WithMany(department => department.DomainGrants)
            .HasForeignKey(item => item.DepartmentId)
            .HasConstraintName("fk_department_domain_grants_departments")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.Domain)
            .WithMany()
            .HasForeignKey(item => item.DomainId)
            .HasConstraintName("fk_department_domain_grants_business_domains")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
