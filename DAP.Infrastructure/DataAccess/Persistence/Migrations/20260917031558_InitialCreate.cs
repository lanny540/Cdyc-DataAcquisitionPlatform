using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAP.Infrastructure.DataAccess.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collection_points",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    protocol = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    communication_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_points", x => x.id);
                    table.CheckConstraint("ck_collection_points_code_not_blank", "btrim(code) <> ''");
                    table.CheckConstraint("ck_collection_points_code_upper", "code = upper(code)");
                    table.CheckConstraint("ck_collection_points_communication_status", "communication_status IN ('在线', '离线', '停用', '未知')");
                    table.CheckConstraint("ck_collection_points_endpoint_not_blank", "btrim(endpoint) <> ''");
                    table.CheckConstraint("ck_collection_points_name_not_blank", "btrim(name) <> ''");
                    table.CheckConstraint("ck_collection_points_protocol_not_blank", "btrim(protocol) <> ''");
                    table.CheckConstraint("ck_collection_points_source", "source IN ('Server', 'Local')");
                });

            migrationBuilder.CreateTable(
                name: "managed_data_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    acquisition_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    connection_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    identifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    process_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    data_category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: ""),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, defaultValue: ""),
                    configuration_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    business_tags_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    collection_interval_seconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 300),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_data_definitions", x => x.id);
                    table.CheckConstraint("ck_managed_data_definitions_acquisition_type_not_blank", "btrim(acquisition_type) <> ''");
                    table.CheckConstraint("ck_managed_data_definitions_code_not_blank", "btrim(code) <> ''");
                    table.CheckConstraint("ck_managed_data_definitions_code_upper", "code = upper(code)");
                    table.CheckConstraint("ck_managed_data_definitions_connection_address_not_blank", "btrim(connection_address) <> ''");
                    table.CheckConstraint("ck_managed_data_definitions_data_category_not_blank", "btrim(data_category) <> ''");
                    table.CheckConstraint("ck_managed_data_definitions_department_not_blank", "btrim(department) <> ''");
                    table.CheckConstraint("ck_managed_data_definitions_identifier_not_blank", "btrim(identifier) <> ''");
                    table.CheckConstraint("ck_managed_data_definitions_interval_seconds_positive", "collection_interval_seconds > 0");
                    table.CheckConstraint("ck_managed_data_definitions_name_not_blank", "btrim(name) <> ''");
                    table.CheckConstraint("ck_managed_data_definitions_process_code_not_blank", "btrim(process_code) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "collection_data_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    metric_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_data_records", x => x.id);
                    table.CheckConstraint("ck_collection_data_records_metric_name_not_blank", "btrim(metric_name) <> ''");
                    table.CheckConstraint("ck_collection_data_records_unit_not_blank", "btrim(unit) <> ''");
                    table.ForeignKey(
                        name: "collection_data_records_collection_point_id_fkey",
                        column: x => x.collection_point_id,
                        principalTable: "collection_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_collection_data_records_collected_at",
                table: "collection_data_records",
                column: "collected_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_collection_data_records_collection_point_id",
                table: "collection_data_records",
                column: "collection_point_id");

            migrationBuilder.CreateIndex(
                name: "ix_collection_data_records_point_metric_collected_at",
                table: "collection_data_records",
                columns: new[] { "collection_point_id", "metric_name", "collected_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_collection_points_source_updated_at",
                table: "collection_points",
                columns: new[] { "source", "updated_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_collection_points_status_updated_at",
                table: "collection_points",
                columns: new[] { "communication_status", "updated_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_collection_points_code",
                table: "collection_points",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_managed_data_definitions_department_process_code",
                table: "managed_data_definitions",
                columns: new[] { "department", "process_code" });

            migrationBuilder.CreateIndex(
                name: "ix_managed_data_definitions_type_enabled_updated_at",
                table: "managed_data_definitions",
                columns: new[] { "acquisition_type", "is_enabled", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_managed_data_definitions_code",
                table: "managed_data_definitions",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_data_records");

            migrationBuilder.DropTable(
                name: "managed_data_definitions");

            migrationBuilder.DropTable(
                name: "collection_points");
        }
    }
}
