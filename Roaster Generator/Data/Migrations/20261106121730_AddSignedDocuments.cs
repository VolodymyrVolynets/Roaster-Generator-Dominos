using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSignedDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "new_signature_requested",
                table: "employee_documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "document_types",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Upload");

            migrationBuilder.CreateTable(
                name: "document_templates",
                columns: table => new
                {
                    document_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    fields = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_templates", x => x.document_type_id);
                    table.ForeignKey(
                        name: "FK_document_templates_document_types_document_type_id",
                        column: x => x.document_type_id,
                        principalTable: "document_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("UPDATE document_types SET kind = 'Sign' WHERE code IN ('spf', 'obu', 'pp');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_templates");

            migrationBuilder.DropColumn(
                name: "new_signature_requested",
                table: "employee_documents");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "document_types");
        }
    }
}
