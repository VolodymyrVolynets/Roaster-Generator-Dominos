using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    // Apply after the existing inside-roster migration on upgrades and fresh databases.
    public partial class AddEmployeeHourlyPay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hourly_rate",
                table: "demand_plans");

            migrationBuilder.DropColumn(
                name: "inside_hourly_rate",
                table: "demand_plans");

            migrationBuilder.AddColumn<decimal>(
                name: "hourly_rate",
                table: "employees",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("00495efa-eb46-43f6-bbe9-fa56b28bb9a2"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("03a5c5c9-8620-49af-b7da-e89c6a7ce123"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("3a27962b-3138-4e8c-9c5c-2fa7c817073f"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("3b64e926-651a-422b-bc62-da27dd846356"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("4777bddb-029a-4248-8393-6bea8a61f7ae"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("4da853a0-b154-4fce-84d3-63512d0ba73f"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("5ad2fc14-cda5-482e-a48a-14bbd43333ed"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("6077cbdb-db89-4643-96ab-a1c2e32cfee1"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("796f9cd8-7132-45e4-b4ce-afdce4476e22"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("877293ba-4650-488a-a0a7-1eff8f5d02f5"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("99c0cddf-f6e8-4bba-a86f-33952523d0bb"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("9e3526b7-d00c-4bd3-b05f-7d15ece9d21b"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("b37367e7-6fed-454b-95c5-646e5d755130"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("b40abf4f-0a67-4a68-b39d-98ce2431efae"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("cfddadab-f4b8-4fbd-945c-4faef19ef2e4"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("d81a3e8a-0a9c-47fd-b568-5cbc2eef9f17"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("e2aeb96b-6937-42ac-8e67-37fe06876a9e"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("f07d963f-272a-4636-a7e5-ee6af713bfa5"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("f7143b02-3df5-4f28-95a5-6946ddfade4f"),
                column: "hourly_rate",
                value: 14.5m);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("fe3538ee-640b-44dc-895e-e859eec2d2c7"),
                column: "hourly_rate",
                value: 14.5m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hourly_rate",
                table: "employees");

            migrationBuilder.AddColumn<decimal>(
                name: "hourly_rate",
                table: "demand_plans",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "inside_hourly_rate",
                table: "demand_plans",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
