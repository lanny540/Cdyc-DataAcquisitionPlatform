using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAP.Infrastructure.DataAccess.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendAcquisitionServerConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "server_connections",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "server_connections",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "last_probe_message",
                table: "server_connections",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_probe_status",
                table: "server_connections",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "未测试");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_probed_at",
                table: "server_connections",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "server_connections");

            migrationBuilder.DropColumn(
                name: "description",
                table: "server_connections");

            migrationBuilder.DropColumn(
                name: "last_probe_message",
                table: "server_connections");

            migrationBuilder.DropColumn(
                name: "last_probe_status",
                table: "server_connections");

            migrationBuilder.DropColumn(
                name: "last_probed_at",
                table: "server_connections");
        }
    }
}
