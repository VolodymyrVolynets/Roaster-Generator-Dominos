using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeRolesAndProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payroll_number",
                table: "employees",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "driver_profiles",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_hours = table.Column<int>(type: "integer", nullable: false, defaultValue: 20),
                    can_work_alone = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    driver_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Car")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_driver_profiles", x => x.employee_id);
                    table.ForeignKey(
                        name: "FK_driver_profiles_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "instore_profiles",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instore_profiles", x => x.employee_id);
                    table.ForeignKey(
                        name: "FK_instore_profiles_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "manager_profiles",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manager_profiles", x => x.employee_id);
                    table.ForeignKey(
                        name: "FK_manager_profiles_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO driver_profiles (employee_id, target_hours, can_work_alone, driver_type)
                SELECT id, target_hours, can_work_alone, 'Car'
                FROM employees;
                """);

            migrationBuilder.DropColumn(
                name: "can_work_alone",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "target_hours",
                table: "employees");

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("00495efa-eb46-43f6-bbe9-fa56b28bb9a2"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("03a5c5c9-8620-49af-b7da-e89c6a7ce123"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("3a27962b-3138-4e8c-9c5c-2fa7c817073f"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("3b64e926-651a-422b-bc62-da27dd846356"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("4777bddb-029a-4248-8393-6bea8a61f7ae"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("4da853a0-b154-4fce-84d3-63512d0ba73f"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("5ad2fc14-cda5-482e-a48a-14bbd43333ed"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("6077cbdb-db89-4643-96ab-a1c2e32cfee1"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("796f9cd8-7132-45e4-b4ce-afdce4476e22"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("877293ba-4650-488a-a0a7-1eff8f5d02f5"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("99c0cddf-f6e8-4bba-a86f-33952523d0bb"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("9e3526b7-d00c-4bd3-b05f-7d15ece9d21b"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("b37367e7-6fed-454b-95c5-646e5d755130"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("b40abf4f-0a67-4a68-b39d-98ce2431efae"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("cfddadab-f4b8-4fbd-945c-4faef19ef2e4"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("d81a3e8a-0a9c-47fd-b568-5cbc2eef9f17"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("e2aeb96b-6937-42ac-8e67-37fe06876a9e"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("f07d963f-272a-4636-a7e5-ee6af713bfa5"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("f7143b02-3df5-4f28-95a5-6946ddfade4f"),
                column: "payroll_number",
                value: null);

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("fe3538ee-640b-44dc-895e-e859eec2d2c7"),
                column: "payroll_number",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_employees_payroll_number",
                table: "employees",
                column: "payroll_number",
                unique: true,
                filter: "payroll_number IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "can_work_alone",
                table: "employees",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "target_hours",
                table: "employees",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.Sql(
                """
                UPDATE employees AS e
                SET target_hours = p.target_hours,
                    can_work_alone = p.can_work_alone
                FROM driver_profiles AS p
                WHERE p.employee_id = e.id;
                """);

            migrationBuilder.DropTable(
                name: "driver_profiles");

            migrationBuilder.DropTable(
                name: "instore_profiles");

            migrationBuilder.DropTable(
                name: "manager_profiles");

            migrationBuilder.DropIndex(
                name: "IX_employees_payroll_number",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "payroll_number",
                table: "employees");

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("00495efa-eb46-43f6-bbe9-fa56b28bb9a2"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("03a5c5c9-8620-49af-b7da-e89c6a7ce123"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("3a27962b-3138-4e8c-9c5c-2fa7c817073f"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("3b64e926-651a-422b-bc62-da27dd846356"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("4777bddb-029a-4248-8393-6bea8a61f7ae"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("4da853a0-b154-4fce-84d3-63512d0ba73f"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("5ad2fc14-cda5-482e-a48a-14bbd43333ed"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("6077cbdb-db89-4643-96ab-a1c2e32cfee1"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("796f9cd8-7132-45e4-b4ce-afdce4476e22"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("877293ba-4650-488a-a0a7-1eff8f5d02f5"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("99c0cddf-f6e8-4bba-a86f-33952523d0bb"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("9e3526b7-d00c-4bd3-b05f-7d15ece9d21b"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("b37367e7-6fed-454b-95c5-646e5d755130"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("b40abf4f-0a67-4a68-b39d-98ce2431efae"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("cfddadab-f4b8-4fbd-945c-4faef19ef2e4"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("d81a3e8a-0a9c-47fd-b568-5cbc2eef9f17"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("e2aeb96b-6937-42ac-8e67-37fe06876a9e"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("f07d963f-272a-4636-a7e5-ee6af713bfa5"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("f7143b02-3df5-4f28-95a5-6946ddfade4f"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });

            migrationBuilder.UpdateData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("fe3538ee-640b-44dc-895e-e859eec2d2c7"),
                columns: new[] { "can_work_alone", "target_hours" },
                values: new object[] { true, 20 });
        }
    }
}
