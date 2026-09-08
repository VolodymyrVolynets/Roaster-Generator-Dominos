using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    public partial class AddHistoricalShiftLengthPreference : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "history_shift_length_weight",
                table: "roster_generation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 100);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "history_shift_length_weight",
                table: "roster_generation_settings");
        }
    }
}
