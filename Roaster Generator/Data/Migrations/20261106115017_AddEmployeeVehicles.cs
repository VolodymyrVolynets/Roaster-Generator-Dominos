using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeVehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_vehicles",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_vehicles", x => x.employee_id);
                    table.ForeignKey(
                        name: "FK_employee_vehicles_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    new_registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_by_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicle_changes", x => x.id);
                    table.ForeignKey(
                        name: "FK_vehicle_changes_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_changes_employee_id",
                table: "vehicle_changes",
                column: "employee_id",
                unique: true,
                filter: "status = 'Open'");

            // The Business Use Insurance document is the reference for existing drivers' vehicles.
            migrationBuilder.Sql("""
                INSERT INTO employee_vehicles (employee_id, registration, updated_at_utc)
                SELECT d.employee_id, d.vehicle_registration, now()
                FROM employee_documents d
                JOIN document_types t ON t.id = d.document_type_id
                WHERE t.code = 'obu' AND d.vehicle_registration IS NOT NULL
                ON CONFLICT (employee_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_vehicles");

            migrationBuilder.DropTable(
                name: "vehicle_changes");
        }
    }
}
