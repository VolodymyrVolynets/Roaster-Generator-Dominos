using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterGenerationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "roster_generation_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_hours_weight = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    long_shift_bonus = table.Column<int>(type: "integer", nullable: false, defaultValue: 25),
                    short_shift_penalty = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    late_finish_penalty = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    early_start_penalty = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roster_generation_settings", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "roster_generation_settings",
                columns: new[] { "id", "early_start_penalty", "late_finish_penalty", "long_shift_bonus", "short_shift_penalty", "target_hours_weight" },
                values: new object[] { new Guid("8f0a9c55-9d8a-4c1c-9a30-1d7e83e8c0f5"), 1, 2, 25, 10, 100 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "roster_generation_settings");
        }
    }
}
