using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DAP.Infrastructure.DataAccess.Persistence.Migrations;

/// <summary>
/// 为跨实例共享的排班快照建立存储表。
/// </summary>
[DbContext(typeof(DataAcquisitionPlatformDbContext))]
[Migration("20261117000000_AddShiftScheduleSnapshot")]
public sealed class AddShiftScheduleSnapshot : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE shift_schedule_snapshot (
                id integer PRIMARY KEY CHECK (id = 1),
                rows_json jsonb NOT NULL,
                imported_at timestamp with time zone NOT NULL,
                file_name text NOT NULL
            )
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE shift_schedule_snapshot");
    }
}
