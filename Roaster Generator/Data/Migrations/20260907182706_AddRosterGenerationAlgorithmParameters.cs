using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterGenerationAlgorithmParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "elite_count",
                table: "roster_generation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "exact_search_node_limit",
                table: "roster_generation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 500000);

            migrationBuilder.AddColumn<int>(
                name: "generation_count",
                table: "roster_generation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 150);

            migrationBuilder.AddColumn<decimal>(
                name: "mutation_rate",
                table: "roster_generation_settings",
                type: "numeric(5,4)",
                nullable: false,
                defaultValue: 0.03m);

            migrationBuilder.AddColumn<int>(
                name: "population_size",
                table: "roster_generation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<int>(
                name: "tournament_size",
                table: "roster_generation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.UpdateData(
                table: "roster_generation_settings",
                keyColumn: "id",
                keyValue: new Guid("8f0a9c55-9d8a-4c1c-9a30-1d7e83e8c0f5"),
                columns: new[] { "elite_count", "exact_search_node_limit", "generation_count", "mutation_rate", "population_size", "tournament_size" },
                values: new object[] { 2, 500000, 150, 0.03m, 24, 2 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "elite_count",
                table: "roster_generation_settings");

            migrationBuilder.DropColumn(
                name: "exact_search_node_limit",
                table: "roster_generation_settings");

            migrationBuilder.DropColumn(
                name: "generation_count",
                table: "roster_generation_settings");

            migrationBuilder.DropColumn(
                name: "mutation_rate",
                table: "roster_generation_settings");

            migrationBuilder.DropColumn(
                name: "population_size",
                table: "roster_generation_settings");

            migrationBuilder.DropColumn(
                name: "tournament_size",
                table: "roster_generation_settings");
        }
    }
}
