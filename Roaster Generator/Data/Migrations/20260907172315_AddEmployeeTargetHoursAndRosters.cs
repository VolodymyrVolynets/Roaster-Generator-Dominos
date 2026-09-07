using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeTargetHoursAndRosters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "target_hours",
                table: "employees",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.CreateTable(
                name: "roster_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roster_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roster_shifts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    roster_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    finish_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roster_shifts", x => x.id);
                    table.ForeignKey(
                        name: "FK_roster_shifts_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_roster_shifts_roster_plans_roster_plan_id",
                        column: x => x.roster_plan_id,
                        principalTable: "roster_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("00495efa-eb46-43f6-bbe9-fa56b28bb9a2"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("03a5c5c9-8620-49af-b7da-e89c6a7ce123"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("3a27962b-3138-4e8c-9c5c-2fa7c817073f"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("3b64e926-651a-422b-bc62-da27dd846356"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("4777bddb-029a-4248-8393-6bea8a61f7ae"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("4da853a0-b154-4fce-84d3-63512d0ba73f"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("5ad2fc14-cda5-482e-a48a-14bbd43333ed"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("6077cbdb-db89-4643-96ab-a1c2e32cfee1"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("796f9cd8-7132-45e4-b4ce-afdce4476e22"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("877293ba-4650-488a-a0a7-1eff8f5d02f5"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("99c0cddf-f6e8-4bba-a86f-33952523d0bb"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("9e3526b7-d00c-4bd3-b05f-7d15ece9d21b"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("b37367e7-6fed-454b-95c5-646e5d755130"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("b40abf4f-0a67-4a68-b39d-98ce2431efae"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("cfddadab-f4b8-4fbd-945c-4faef19ef2e4"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("d81a3e8a-0a9c-47fd-b568-5cbc2eef9f17"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("e2aeb96b-6937-42ac-8e67-37fe06876a9e"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("f07d963f-272a-4636-a7e5-ee6af713bfa5"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("f7143b02-3df5-4f28-95a5-6946ddfade4f"),
                column: "target_hours",
                value: 20);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("fe3538ee-640b-44dc-895e-e859eec2d2c7"),
                column: "target_hours",
                value: 20);

            migrationBuilder.CreateIndex(
                name: "IX_roster_plans_week_start",
                table: "roster_plans",
                column: "week_start",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_roster_shifts_employee_id",
                table: "roster_shifts",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "IX_roster_shifts_roster_plan_id_employee_id_date_start_time_fi~",
                table: "roster_shifts",
                columns: new[] { "roster_plan_id", "employee_id", "date", "start_time", "finish_time" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "roster_shifts");

            migrationBuilder.DropTable(
                name: "roster_plans");

            migrationBuilder.DropColumn(
                name: "target_hours",
                table: "employees");
        }
    }
}
