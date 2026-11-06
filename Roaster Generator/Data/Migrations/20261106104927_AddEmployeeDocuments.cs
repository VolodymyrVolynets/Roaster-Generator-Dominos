using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    document_type_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    analysis_result = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_drafts", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_drafts_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    expiry_rule = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    has_reference = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_requirements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    document_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    condition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_requirements", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_requirements_document_types_document_type_id",
                        column: x => x.document_type_id,
                        principalTable: "document_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    holder_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reference_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    issuing_country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    stamp = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    vehicle_registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    signing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    penalty_points = table.Column<int>(type: "integer", nullable: true),
                    review_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Clear"),
                    review_reasons = table.Column<List<string>>(type: "text[]", nullable: false),
                    employee_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    uploaded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_employee_documents_document_types_document_type_id",
                        column: x => x.document_type_id,
                        principalTable: "document_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_documents_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_document_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    draft_id = table.Column<Guid>(type: "uuid", nullable: true),
                    page_index = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    encrypted_content = table.Column<byte[]>(type: "bytea", nullable: false),
                    nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    key_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_document_files", x => x.id);
                    table.CheckConstraint("ck_employee_document_files_one_owner", "(document_id IS NULL) <> (draft_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_employee_document_files_document_drafts_draft_id",
                        column: x => x.draft_id,
                        principalTable: "document_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_employee_document_files_employee_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "employee_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_audit_events_document_id",
                table: "document_audit_events",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_audit_events_employee_id_occurred_at_utc",
                table: "document_audit_events",
                columns: new[] { "employee_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_document_drafts_employee_id",
                table: "document_drafts",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_drafts_expires_at_utc",
                table: "document_drafts",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_document_requirements_category_document_type_id",
                table: "document_requirements",
                columns: new[] { "category", "document_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_requirements_document_type_id",
                table: "document_requirements",
                column: "document_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_types_code",
                table: "document_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_document_files_document_id_page_index",
                table: "employee_document_files",
                columns: new[] { "document_id", "page_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_document_files_draft_id_page_index",
                table: "employee_document_files",
                columns: new[] { "draft_id", "page_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_documents_document_type_id",
                table: "employee_documents",
                column: "document_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_documents_employee_id_document_type_id",
                table: "employee_documents",
                columns: new[] { "employee_id", "document_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_documents_expiry_date",
                table: "employee_documents",
                column: "expiry_date");

            migrationBuilder.CreateIndex(
                name: "IX_employee_documents_review_status",
                table: "employee_documents",
                column: "review_status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_audit_events");

            migrationBuilder.DropTable(
                name: "document_requirements");

            migrationBuilder.DropTable(
                name: "employee_document_files");

            migrationBuilder.DropTable(
                name: "document_drafts");

            migrationBuilder.DropTable(
                name: "employee_documents");

            migrationBuilder.DropTable(
                name: "document_types");
        }
    }
}
