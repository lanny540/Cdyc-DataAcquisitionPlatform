using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAP.Infrastructure.DataAccess.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnergyDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "energy_media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_primary_energy = table.Column<bool>(type: "boolean", nullable: false),
                    coal_factor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_energy_media", x => x.id);
                    table.CheckConstraint("ck_energy_media_coal_factor", "coal_factor is null or coal_factor >= 0");
                    table.CheckConstraint("ck_energy_media_code_not_blank", "btrim(code) <> ''");
                    table.CheckConstraint("ck_energy_media_name_not_blank", "btrim(name) <> ''");
                    table.CheckConstraint("ck_energy_media_unit_not_blank", "btrim(unit) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "energy_org_units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_energy_org_units", x => x.id);
                    table.CheckConstraint("ck_energy_org_units_code_not_blank", "btrim(code) <> ''");
                    table.CheckConstraint("ck_energy_org_units_level", "level in ('park', 'department', 'process')");
                    table.CheckConstraint("ck_energy_org_units_name_not_blank", "btrim(name) <> ''");
                    table.CheckConstraint("ck_energy_org_units_parent_by_level", "(level = 'park' and parent_id is null) or (level <> 'park' and parent_id is not null)");
                    table.ForeignKey(
                        name: "fk_energy_org_units_departments",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_energy_org_units_parent",
                        column: x => x.parent_id,
                        principalTable: "energy_org_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "energy_systems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_energy_systems", x => x.id);
                    table.CheckConstraint("ck_energy_systems_code_not_blank", "btrim(code) <> ''");
                    table.CheckConstraint("ck_energy_systems_name_not_blank", "btrim(name) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "energy_usage_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_energy_usage_types", x => x.id);
                    table.CheckConstraint("ck_energy_usage_types_code_not_blank", "btrim(code) <> ''");
                    table.CheckConstraint("ck_energy_usage_types_name_not_blank", "btrim(name) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "energy_meters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    medium_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usage_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    system_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_meter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_summary = table.Column<bool>(type: "boolean", nullable: false),
                    ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    is_cumulative = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_energy_meters", x => x.id);
                    table.CheckConstraint("ck_energy_meters_code_not_blank", "btrim(code) <> ''");
                    table.CheckConstraint("ck_energy_meters_name_not_blank", "btrim(name) <> ''");
                    table.CheckConstraint("ck_energy_meters_ratio_positive", "ratio > 0");
                    table.ForeignKey(
                        name: "fk_energy_meters_energy_media",
                        column: x => x.medium_id,
                        principalTable: "energy_media",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_energy_meters_energy_org_units",
                        column: x => x.org_unit_id,
                        principalTable: "energy_org_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_energy_meters_energy_systems",
                        column: x => x.system_type_id,
                        principalTable: "energy_systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_energy_meters_energy_usage_types",
                        column: x => x.usage_type_id,
                        principalTable: "energy_usage_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_energy_meters_parent",
                        column: x => x.parent_meter_id,
                        principalTable: "energy_meters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "energy_readings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    meter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_energy_readings", x => x.id);
                    table.CheckConstraint("ck_energy_readings_source", "source in ('manual', 'collection', 'import')");
                    table.ForeignKey(
                        name: "fk_energy_readings_energy_meters",
                        column: x => x.meter_id,
                        principalTable: "energy_meters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_energy_media_code",
                table: "energy_media",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_energy_meters_medium_id",
                table: "energy_meters",
                column: "medium_id");

            migrationBuilder.CreateIndex(
                name: "ix_energy_meters_org_unit_id",
                table: "energy_meters",
                column: "org_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_energy_meters_parent_meter_id",
                table: "energy_meters",
                column: "parent_meter_id");

            migrationBuilder.CreateIndex(
                name: "ix_energy_meters_system_type_id",
                table: "energy_meters",
                column: "system_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_energy_meters_usage_type_id",
                table: "energy_meters",
                column: "usage_type_id");

            migrationBuilder.CreateIndex(
                name: "ux_energy_meters_code",
                table: "energy_meters",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_energy_org_units_department_id",
                table: "energy_org_units",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_energy_org_units_parent_id",
                table: "energy_org_units",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ux_energy_org_units_code",
                table: "energy_org_units",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_energy_readings_meter_read_at",
                table: "energy_readings",
                columns: new[] { "meter_id", "read_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_energy_systems_code",
                table: "energy_systems",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_energy_usage_types_code",
                table: "energy_usage_types",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "energy_readings");

            migrationBuilder.DropTable(
                name: "energy_meters");

            migrationBuilder.DropTable(
                name: "energy_media");

            migrationBuilder.DropTable(
                name: "energy_org_units");

            migrationBuilder.DropTable(
                name: "energy_systems");

            migrationBuilder.DropTable(
                name: "energy_usage_types");
        }
    }
}
