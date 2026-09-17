using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAP.Infrastructure.DataAccess.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkManagedDefinitionsToServerConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "server_connection_id",
                table: "managed_data_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_managed_data_definitions_server_connection_id",
                table: "managed_data_definitions",
                column: "server_connection_id");

            migrationBuilder.AddForeignKey(
                name: "managed_data_definitions_server_connection_id_fkey",
                table: "managed_data_definitions",
                column: "server_connection_id",
                principalTable: "server_connections",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(
                """
                UPDATE managed_data_definitions d
                SET server_connection_id = s.id
                FROM server_connections s
                WHERE d.acquisition_type = s.acquisition_type
                  AND rtrim(d.connection_address, '/') = s.address;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "managed_data_definitions_server_connection_id_fkey",
                table: "managed_data_definitions");

            migrationBuilder.DropIndex(
                name: "ix_managed_data_definitions_server_connection_id",
                table: "managed_data_definitions");

            migrationBuilder.DropColumn(
                name: "server_connection_id",
                table: "managed_data_definitions");
        }
    }
}
