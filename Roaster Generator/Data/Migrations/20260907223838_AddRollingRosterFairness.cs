using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRollingRosterFairness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "fairness_spread_weight",
                table: "roster_generation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.AddColumn<int>(
                name: "history_fairness_weight",
                table: "roster_generation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.UpdateData(
                table: "roster_generation_settings",
                keyColumn: "id",
                keyValue: new Guid("8f0a9c55-9d8a-4c1c-9a30-1d7e83e8c0f5"),
                columns: new[] { "fairness_spread_weight", "history_fairness_weight" },
                values: new object[] { 1000, 100 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fairness_spread_weight",
                table: "roster_generation_settings");

            migrationBuilder.DropColumn(
                name: "history_fairness_weight",
                table: "roster_generation_settings");
        }
    }
}
