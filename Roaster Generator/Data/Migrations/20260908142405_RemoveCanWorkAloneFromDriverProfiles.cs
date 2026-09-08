using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCanWorkAloneFromDriverProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE driver_profiles
                SET driver_type = CASE WHEN can_work_alone THEN 'Car' ELSE 'EBike' END;
                """);

            migrationBuilder.DropColumn(
                name: "can_work_alone",
                table: "driver_profiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "can_work_alone",
                table: "driver_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql(
                """
                UPDATE driver_profiles
                SET can_work_alone = (driver_type = 'Car');
                """);
        }
    }
}
