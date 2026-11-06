using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddManagerSignOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "awaiting_manager_signature",
                table: "employee_documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "manager_signed_at_utc",
                table: "employee_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "manager_signed_by_name",
                table: "employee_documents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "awaiting_manager_signature",
                table: "employee_documents");

            migrationBuilder.DropColumn(
                name: "manager_signed_at_utc",
                table: "employee_documents");

            migrationBuilder.DropColumn(
                name: "manager_signed_by_name",
                table: "employee_documents");
        }
    }
}
