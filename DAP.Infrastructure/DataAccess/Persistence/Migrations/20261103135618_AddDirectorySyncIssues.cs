using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAP.Infrastructure.DataAccess.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectorySyncIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "directory_sync_issues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    issue_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    external_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sample_user_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    occurrences = table.Column<int>(type: "integer", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_directory_sync_issues", x => x.id);
                    table.CheckConstraint("ck_directory_sync_issues_external_code_not_blank", "btrim(external_code) <> ''");
                    table.CheckConstraint("ck_directory_sync_issues_occurrences", "occurrences >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_directory_sync_issues_last_seen_at",
                table: "directory_sync_issues",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "ux_directory_sync_issues_pending",
                table: "directory_sync_issues",
                columns: new[] { "provider", "issue_type", "external_code" },
                unique: true,
                filter: "resolved_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "directory_sync_issues");
        }
    }
}
