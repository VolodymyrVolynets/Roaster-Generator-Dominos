using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomaticManagerSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_manager_signatures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    manager_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    encrypted_content = table.Column<byte[]>(type: "bytea", nullable: false),
                    nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    key_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    saved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_manager_signatures", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_manager_signatures");
        }
    }
}
