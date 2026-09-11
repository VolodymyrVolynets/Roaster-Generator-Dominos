using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Roaster_Generator.Data;

#nullable disable

namespace Roaster_Generator.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260911093000_AddSickLeaveRequests")]
public sealed class AddSickLeaveRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sick_leave_requests",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                start_date = table.Column<DateOnly>(type: "date", nullable: false),
                finish_date = table.Column<DateOnly>(type: "date", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Requested"),
                attachment_content = table.Column<byte[]>(type: "bytea", nullable: true),
                attachment_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                attachment_content_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                attachment_file_size = table.Column<long>(type: "bigint", nullable: true),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reviewed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                reviewed_by_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                attachment_deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revision = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sick_leave_requests", x => x.id);
                table.ForeignKey(
                    name: "FK_sick_leave_requests_employees_employee_id",
                    column: x => x.employee_id,
                    principalTable: "employees",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_sick_leave_requests_employee_id_status",
            table: "sick_leave_requests",
            columns: new[] { "employee_id", "status" });
        migrationBuilder.CreateIndex(
            name: "IX_sick_leave_requests_start_date_finish_date",
            table: "sick_leave_requests",
            columns: new[] { "start_date", "finish_date" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "sick_leave_requests");
}
