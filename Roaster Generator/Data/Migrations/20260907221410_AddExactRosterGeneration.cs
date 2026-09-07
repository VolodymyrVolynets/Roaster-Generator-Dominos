using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExactRosterGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "snapshot_json",
                table: "roster_plans",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_solve_seconds",
                table: "roster_generation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "minimum_rest_hours",
                table: "roster_generation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 8);

            migrationBuilder.AddColumn<int>(
                name: "preferred_rest_hours",
                table: "roster_generation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.UpdateData(
                table: "roster_generation_settings",
                keyColumn: "id",
                keyValue: new Guid("8f0a9c55-9d8a-4c1c-9a30-1d7e83e8c0f5"),
                columns: new[] { "max_solve_seconds", "minimum_rest_hours", "preferred_rest_hours" },
                values: new object[] { 20, 8, 12 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "snapshot_json",
                table: "roster_plans");

            migrationBuilder.DropColumn(
                name: "max_solve_seconds",
                table: "roster_generation_settings");

            migrationBuilder.DropColumn(
                name: "minimum_rest_hours",
                table: "roster_generation_settings");

            migrationBuilder.DropColumn(
                name: "preferred_rest_hours",
                table: "roster_generation_settings");
        }
    }
}
