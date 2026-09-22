using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAbsentForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "absent_forms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_full_name = table.Column<string>(type: "character varying(201)", maxLength: 201, nullable: false),
                    payroll_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    saved_roster_shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shift_date = table.Column<DateOnly>(type: "date", nullable: false),
                    shift_start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    shift_finish_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    notification_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notification_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    notification_method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cancellation_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_absent_forms", x => x.id);
                    table.ForeignKey(
                        name: "FK_absent_forms_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_absent_forms_employee_id_notification_date_notification_time",
                table: "absent_forms",
                columns: new[] { "employee_id", "notification_date", "notification_time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "absent_forms");
        }
    }
}
