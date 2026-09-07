using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations;

public partial class OptimizeEvolutionarySearch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "daily_option_pool_size",
            table: "roster_generation_settings",
            type: "integer",
            nullable: false,
            defaultValue: 256);

        migrationBuilder.AddColumn<int>(
            name: "daily_option_search_node_limit",
            table: "roster_generation_settings",
            type: "integer",
            nullable: false,
            defaultValue: 100000);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "daily_option_pool_size",
            table: "roster_generation_settings");

        migrationBuilder.DropColumn(
            name: "daily_option_search_node_limit",
            table: "roster_generation_settings");
    }
}
