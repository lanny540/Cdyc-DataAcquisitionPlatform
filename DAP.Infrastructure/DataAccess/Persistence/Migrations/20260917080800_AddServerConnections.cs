using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAP.Infrastructure.DataAccess.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServerConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "server_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    acquisition_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    configuration_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_server_connections", x => x.id);
                    table.CheckConstraint("ck_server_connections_acquisition_type_not_blank", "btrim(acquisition_type) <> ''");
                    table.CheckConstraint("ck_server_connections_address_not_blank", "btrim(address) <> ''");
                    table.CheckConstraint("ck_server_connections_code_not_blank", "btrim(code) <> ''");
                    table.CheckConstraint("ck_server_connections_code_upper", "code = upper(code)");
                    table.CheckConstraint("ck_server_connections_display_name_not_blank", "btrim(display_name) <> ''");
                });

            migrationBuilder.CreateIndex(
                name: "ix_server_connections_type_enabled_updated_at",
                table: "server_connections",
                columns: new[] { "acquisition_type", "is_enabled", "updated_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_server_connections_code",
                table: "server_connections",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_server_connections_type_address",
                table: "server_connections",
                columns: new[] { "acquisition_type", "address" },
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO server_connections (
                    id,
                    code,
                    display_name,
                    acquisition_type,
                    address,
                    configuration_json,
                    is_enabled,
                    updated_at)
                SELECT DISTINCT ON (d.acquisition_type, rtrim(d.connection_address, '/'))
                    (
                        substr(md5(d.acquisition_type || '|' || rtrim(d.connection_address, '/')), 1, 8) || '-' ||
                        substr(md5(d.acquisition_type || '|' || rtrim(d.connection_address, '/')), 9, 4) || '-' ||
                        substr(md5(d.acquisition_type || '|' || rtrim(d.connection_address, '/')), 13, 4) || '-' ||
                        substr(md5(d.acquisition_type || '|' || rtrim(d.connection_address, '/')), 17, 4) || '-' ||
                        substr(md5(d.acquisition_type || '|' || rtrim(d.connection_address, '/')), 21, 12)
                    )::uuid,
                    'SRV_' || upper(substr(md5(d.acquisition_type || '|' || rtrim(d.connection_address, '/')), 1, 60)),
                    coalesce(
                        nullif(d.configuration_json ->> 'serverName', ''),
                        d.acquisition_type || ' 服务器'),
                    d.acquisition_type,
                    rtrim(d.connection_address, '/'),
                    d.configuration_json,
                    d.is_enabled,
                    d.updated_at
                FROM managed_data_definitions d
                WHERE btrim(d.connection_address) <> ''
                ORDER BY
                    d.acquisition_type,
                    rtrim(d.connection_address, '/'),
                    d.is_enabled DESC,
                    d.updated_at DESC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "server_connections");
        }
    }
}
