using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Roaster_Generator.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261012203000_AddPasswordChangeStatus")]
public sealed class AddPasswordChangeStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<bool>(name: "HasChangedPassword", table: "AspNetUsers",
            type: "boolean", nullable: false, defaultValue: false);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "HasChangedPassword", table: "AspNetUsers");
}
