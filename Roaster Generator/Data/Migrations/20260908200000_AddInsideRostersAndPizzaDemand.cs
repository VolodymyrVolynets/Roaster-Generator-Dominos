using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    // Follows the existing historical shift-length migration on both upgrades and fresh databases.
    public partial class AddInsideRostersAndPizzaDemand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_roster_plans_week_start",
                table: "roster_plans");

            migrationBuilder.AddColumn<string>(
                name: "roster_kind",
                table: "roster_plans",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "drivers");

            migrationBuilder.AddColumn<int>(
                name: "target_hours",
                table: "manager_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "target_hours",
                table: "instore_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "inside_demand",
                table: "demand_values",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "pizzas",
                table: "demand_values",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "deliveries_per_driver_hour",
                table: "demand_plans",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: false,
                defaultValue: 2.7m);

            migrationBuilder.AddColumn<decimal>(
                name: "inside_hourly_rate",
                table: "demand_plans",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "pizzas_per_inside_hour",
                table: "demand_plans",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: false,
                defaultValue: 20m);

            migrationBuilder.CreateIndex(
                name: "IX_roster_plans_week_start_roster_kind",
                table: "roster_plans",
                columns: new[] { "week_start", "roster_kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_roster_plans_week_start_roster_kind",
                table: "roster_plans");

            migrationBuilder.DropColumn(
                name: "roster_kind",
                table: "roster_plans");

            migrationBuilder.DropColumn(
                name: "target_hours",
                table: "manager_profiles");

            migrationBuilder.DropColumn(
                name: "target_hours",
                table: "instore_profiles");

            migrationBuilder.DropColumn(
                name: "inside_demand",
                table: "demand_values");

            migrationBuilder.DropColumn(
                name: "pizzas",
                table: "demand_values");

            migrationBuilder.DropColumn(
                name: "deliveries_per_driver_hour",
                table: "demand_plans");

            migrationBuilder.DropColumn(
                name: "inside_hourly_rate",
                table: "demand_plans");

            migrationBuilder.DropColumn(
                name: "pizzas_per_inside_hour",
                table: "demand_plans");

            migrationBuilder.CreateIndex(
                name: "IX_roster_plans_week_start",
                table: "roster_plans",
                column: "week_start",
                unique: true);
        }
    }
}
