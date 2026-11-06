using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFranchiseeName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "franchisee_name",
                table: "store_settings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "franchisee_name",
                table: "store_settings");
        }
    }
}
