using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDemandManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "demand_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demand_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "demand_columns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    demand_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demand_columns", x => x.id);
                    table.ForeignKey(
                        name: "FK_demand_columns_demand_plans_demand_plan_id",
                        column: x => x.demand_plan_id,
                        principalTable: "demand_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "demand_rows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    demand_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hour = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demand_rows", x => x.id);
                    table.ForeignKey(
                        name: "FK_demand_rows_demand_plans_demand_plan_id",
                        column: x => x.demand_plan_id,
                        principalTable: "demand_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "demand_values",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    demand_row_id = table.Column<Guid>(type: "uuid", nullable: false),
                    demand_column_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pizzas = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    deliveries = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    demand = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demand_values", x => x.id);
                    table.ForeignKey(
                        name: "FK_demand_values_demand_columns_demand_column_id",
                        column: x => x.demand_column_id,
                        principalTable: "demand_columns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_demand_values_demand_rows_demand_row_id",
                        column: x => x.demand_row_id,
                        principalTable: "demand_rows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_demand_columns_demand_plan_id_position",
                table: "demand_columns",
                columns: new[] { "demand_plan_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_demand_plans_week_start",
                table: "demand_plans",
                column: "week_start",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_demand_rows_demand_plan_id_hour",
                table: "demand_rows",
                columns: new[] { "demand_plan_id", "hour" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_demand_values_demand_column_id",
                table: "demand_values",
                column: "demand_column_id");

            migrationBuilder.CreateIndex(
                name: "IX_demand_values_demand_row_id_demand_column_id",
                table: "demand_values",
                columns: new[] { "demand_row_id", "demand_column_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "demand_values");

            migrationBuilder.DropTable(
                name: "demand_columns");

            migrationBuilder.DropTable(
                name: "demand_rows");

            migrationBuilder.DropTable(
                name: "demand_plans");
        }
    }
}
