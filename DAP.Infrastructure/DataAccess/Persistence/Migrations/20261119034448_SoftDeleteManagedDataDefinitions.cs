using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAP.Infrastructure.DataAccess.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteManagedDataDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_managed_data_definitions_code",
                table: "managed_data_definitions");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "managed_data_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_managed_data_definitions_code",
                table: "managed_data_definitions",
                column: "code",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_managed_data_definitions_code",
                table: "managed_data_definitions");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "managed_data_definitions");

            migrationBuilder.CreateIndex(
                name: "ux_managed_data_definitions_code",
                table: "managed_data_definitions",
                column: "code",
                unique: true);
        }
    }
}
