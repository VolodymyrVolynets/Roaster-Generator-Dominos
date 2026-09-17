using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyVehicleCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "company_cars",
                table: "roster_generation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "company_ebikes",
                table: "roster_generation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "company_mopeds",
                table: "roster_generation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_own",
                table: "driver_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.UpdateData(
                table: "roster_generation_settings",
                keyColumn: "id",
                keyValue: new Guid("8f0a9c55-9d8a-4c1c-9a30-1d7e83e8c0f5"),
                columns: new[] { "company_cars", "company_ebikes", "company_mopeds" },
                values: new object[] { 0, 0, 0 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "company_cars",
                table: "roster_generation_settings");

            migrationBuilder.DropColumn(
                name: "company_ebikes",
                table: "roster_generation_settings");

            migrationBuilder.DropColumn(
                name: "company_mopeds",
                table: "roster_generation_settings");

            migrationBuilder.DropColumn(
                name: "is_own",
                table: "driver_profiles");
        }
    }
}
