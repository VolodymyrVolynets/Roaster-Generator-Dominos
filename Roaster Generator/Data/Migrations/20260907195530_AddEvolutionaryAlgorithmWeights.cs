using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations;

public partial class AddEvolutionaryAlgorithmWeights : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "daily_shift_count_penalty",
            table: "roster_generation_settings",
            type: "integer",
            nullable: false,
            defaultValue: 25);

        migrationBuilder.AddColumn<int>(
            name: "short_break_penalty",
            table: "roster_generation_settings",
            type: "integer",
            nullable: false,
            defaultValue: 100);

        migrationBuilder.DropColumn(
            name: "early_start_penalty",
            table: "roster_generation_settings");

        migrationBuilder.DropColumn(
            name: "late_finish_penalty",
            table: "roster_generation_settings");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "early_start_penalty",
            table: "roster_generation_settings",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<int>(
            name: "late_finish_penalty",
            table: "roster_generation_settings",
            type: "integer",
            nullable: false,
            defaultValue: 2);

        migrationBuilder.DropColumn(
            name: "daily_shift_count_penalty",
            table: "roster_generation_settings");

        migrationBuilder.DropColumn(
            name: "short_break_penalty",
            table: "roster_generation_settings");
    }
}
