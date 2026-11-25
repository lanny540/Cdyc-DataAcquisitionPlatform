using Microsoft.EntityFrameworkCore.Migrations;

namespace DAP.Infrastructure.DataAccess.Persistence.Migrations;

/// <summary>
/// 为大屏导航枢纽页（根路由）建立跳转卡片存储表。
/// </summary>
public partial class AddScreenHubCards : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE screen_hub_cards (
                id uuid PRIMARY KEY,
                title text NOT NULL,
                description text NOT NULL,
                route text NOT NULL,
                icon_key text NOT NULL,
                accent text NOT NULL,
                sort_order integer NOT NULL,
                is_enabled boolean NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                CONSTRAINT ck_screen_hub_cards_title_not_blank CHECK (btrim(title) <> ''),
                CONSTRAINT ck_screen_hub_cards_route_not_blank CHECK (btrim(route) <> '')
            );
            CREATE INDEX ix_screen_hub_cards_sort_order ON screen_hub_cards (sort_order);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS screen_hub_cards;");
    }
}
