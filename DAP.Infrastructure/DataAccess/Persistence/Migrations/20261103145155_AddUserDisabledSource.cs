using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAP.Infrastructure.DataAccess.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDisabledSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "disabled_source",
                table: "app_users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_app_users_disabled_source",
                table: "app_users",
                sql: "disabled_source is null or disabled_source in ('manual', 'directory')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_app_users_disabled_source",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "disabled_source",
                table: "app_users");
        }
    }
}
