using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations;

public partial class AddFairHoursAlpha : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "fair_hours_alpha",
            table: "roster_generation_settings",
            type: "numeric(3,2)",
            nullable: false,
            defaultValue: 0.7m);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "fair_hours_alpha",
            table: "roster_generation_settings");
    }
}
