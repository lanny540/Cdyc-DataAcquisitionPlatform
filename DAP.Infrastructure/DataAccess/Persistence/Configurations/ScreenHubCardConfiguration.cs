using DAP.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DAP.Infrastructure.DataAccess.Persistence.Configurations;

/// <summary>
/// 配置大屏导航卡实体映射。
/// </summary>
public sealed class ScreenHubCardConfiguration : IEntityTypeConfiguration<ScreenHubCard>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ScreenHubCard> builder)
    {
        builder.ToTable(
            "screen_hub_cards",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("ck_screen_hub_cards_title_not_blank", "btrim(title) <> ''");
                tableBuilder.HasCheckConstraint("ck_screen_hub_cards_route_not_blank", "btrim(route) <> ''");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Title).HasColumnName("title").HasMaxLength(64).IsRequired();
        builder.Property(item => item.Description).HasColumnName("description").HasMaxLength(256).IsRequired();
        builder.Property(item => item.Route).HasColumnName("route").HasMaxLength(200).IsRequired();
        builder.Property(item => item.IconKey).HasColumnName("icon_key").HasMaxLength(64).IsRequired();
        builder.Property(item => item.Accent).HasColumnName("accent").HasMaxLength(32).IsRequired();
        builder.Property(item => item.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(item => item.SortOrder)
            .HasDatabaseName("ix_screen_hub_cards_sort_order");
    }
}
