using DAP.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DAP.Infrastructure.DataAccess.Persistence.Configurations;

/// <summary>
/// 配置目录同步待处理项映射。
/// </summary>
public sealed class DirectorySyncIssueConfiguration : IEntityTypeConfiguration<DirectorySyncIssue>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DirectorySyncIssue> builder)
    {
        builder.ToTable(
            "directory_sync_issues",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_directory_sync_issues_external_code_not_blank",
                    "btrim(external_code) <> ''");
                tableBuilder.HasCheckConstraint(
                    "ck_directory_sync_issues_occurrences",
                    "occurrences >= 1");
            });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
        builder.Property(item => item.IssueType).HasColumnName("issue_type").HasMaxLength(50).IsRequired();
        builder.Property(item => item.ExternalCode).HasColumnName("external_code").HasMaxLength(200).IsRequired();
        builder.Property(item => item.SampleUserName).HasColumnName("sample_user_name").HasMaxLength(100);
        builder.Property(item => item.Occurrences).HasColumnName("occurrences").IsRequired();
        builder.Property(item => item.FirstSeenAt).HasColumnName("first_seen_at").IsRequired();
        builder.Property(item => item.LastSeenAt).HasColumnName("last_seen_at").IsRequired();
        builder.Property(item => item.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(item => item.ResolvedBy).HasColumnName("resolved_by");

        // 未处理项唯一：同一部门/同一账号名只留一行，靠 occurrences 计数。
        // 已处理的允许重复建行——处理完又被目录带回来，是新一轮问题，不该被历史行吞掉。
        builder.HasIndex(item => new {item.Provider, item.IssueType, item.ExternalCode})
            .IsUnique()
            .HasDatabaseName("ux_directory_sync_issues_pending")
            .HasFilter("resolved_at IS NULL");

        builder.HasIndex(item => item.LastSeenAt)
            .HasDatabaseName("ix_directory_sync_issues_last_seen_at");
    }
}
