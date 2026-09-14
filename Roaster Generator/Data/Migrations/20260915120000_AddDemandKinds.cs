using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations;

public partial class AddDemandKinds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_demand_plans_week_start",
            table: "demand_plans");

        migrationBuilder.AddColumn<string>(
            name: "demand_kind",
            table: "demand_plans",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "outside");

        migrationBuilder.CreateIndex(
            name: "IX_demand_plans_week_start_demand_kind",
            table: "demand_plans",
            columns: new[] { "week_start", "demand_kind" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_demand_plans_week_start_demand_kind",
            table: "demand_plans");

        migrationBuilder.DropColumn(
            name: "demand_kind",
            table: "demand_plans");

        migrationBuilder.CreateIndex(
            name: "IX_demand_plans_week_start",
            table: "demand_plans",
            column: "week_start",
            unique: true);
    }
}
