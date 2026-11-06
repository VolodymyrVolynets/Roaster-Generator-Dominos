using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations;

public partial class PreserveRemovedEmployeeHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_employees_employee_number", table: "employees");
        migrationBuilder.DropIndex(name: "IX_employees_payroll_number", table: "employees");
        migrationBuilder.AddColumn<bool>(name: "is_deleted", table: "employees",
            type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.CreateIndex(name: "IX_employees_employee_number", table: "employees",
            column: "employee_number", unique: true, filter: "is_deleted = false");
        migrationBuilder.CreateIndex(name: "IX_employees_payroll_number", table: "employees",
            column: "payroll_number", unique: true, filter: "payroll_number IS NOT NULL AND is_deleted = false");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new System.NotSupportedException(
            "Employee archival cannot be automatically rolled back: employee and payroll numbers may have been reused. " +
            "A manual data-preserving migration is required.");
    }
}
