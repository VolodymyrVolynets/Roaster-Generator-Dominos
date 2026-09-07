using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Roaster_Generator.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedEmployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "employees",
                columns: new[] { "id", "employee_number", "first_name", "last_name", "phone_number" },
                values: new object[,]
                {
                    { new Guid("00495efa-eb46-43f6-bbe9-fa56b28bb9a2"), "7870", "Arshad B", "omarkhel", "0899427870" },
                    { new Guid("03a5c5c9-8620-49af-b7da-e89c6a7ce123"), "2458", "Chowdhury", "Belal", "0894582680" },
                    { new Guid("3a27962b-3138-4e8c-9c5c-2fa7c817073f"), "9100", "Marcin", "Galkowski", "0858340019" },
                    { new Guid("3b64e926-651a-422b-bc62-da27dd846356"), "2075", "Shahram B", "Sajawal", "0858402075" },
                    { new Guid("4777bddb-029a-4248-8393-6bea8a61f7ae"), "2004", "Subhan", "Aqeel", "0831243953" },
                    { new Guid("4da853a0-b154-4fce-84d3-63512d0ba73f"), "3889", "Pradeep", "Sreekumari", "0894133889" },
                    { new Guid("5ad2fc14-cda5-482e-a48a-14bbd43333ed"), "6116", "Tony", "Lukose", "0894466116" },
                    { new Guid("6077cbdb-db89-4643-96ab-a1c2e32cfee1"), "2138", "Nanthu", "Njaneswaran", "0872462138" },
                    { new Guid("796f9cd8-7132-45e4-b4ce-afdce4476e22"), "5512", "Rajendran", "Pandiarajan", "0892315512" },
                    { new Guid("877293ba-4650-488a-a0a7-1eff8f5d02f5"), "9627", "Joveski", "Jovche", "0894262021" },
                    { new Guid("99c0cddf-f6e8-4bba-a86f-33952523d0bb"), "5062", "Shajahan", "Shajahan", "0894045062" },
                    { new Guid("9e3526b7-d00c-4bd3-b05f-7d15ece9d21b"), "3512", "Bibin", "Baby", "0892403512" },
                    { new Guid("b37367e7-6fed-454b-95c5-646e5d755130"), "0741", "Ronan", "O'Dwyer", "0852860741" },
                    { new Guid("b40abf4f-0a67-4a68-b39d-98ce2431efae"), "3040", "Jomon", "Thottiparambil Johny", "0874520403" },
                    { new Guid("cfddadab-f4b8-4fbd-945c-4faef19ef2e4"), "7090", "WEI", "WANG", "0870570907" },
                    { new Guid("d81a3e8a-0a9c-47fd-b568-5cbc2eef9f17"), "1840", "ANDREWS", "ABRAHAM CHACKO", "0831431840" },
                    { new Guid("e2aeb96b-6937-42ac-8e67-37fe06876a9e"), "5385", "Mustafa B", "Kanchwala", "0894915385" },
                    { new Guid("f07d963f-272a-4636-a7e5-ee6af713bfa5"), "8944", "Alban", "Keane", "0858388944" },
                    { new Guid("f7143b02-3df5-4f28-95a5-6946ddfade4f"), "7179", "SunilValliparampil", "Alex", "0892097179" },
                    { new Guid("fe3538ee-640b-44dc-895e-e859eec2d2c7"), "7229", "Pavishkumar B", "Kumararamalingam", "0892597229" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("00495efa-eb46-43f6-bbe9-fa56b28bb9a2"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("03a5c5c9-8620-49af-b7da-e89c6a7ce123"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("3a27962b-3138-4e8c-9c5c-2fa7c817073f"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("3b64e926-651a-422b-bc62-da27dd846356"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("4777bddb-029a-4248-8393-6bea8a61f7ae"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("4da853a0-b154-4fce-84d3-63512d0ba73f"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("5ad2fc14-cda5-482e-a48a-14bbd43333ed"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("6077cbdb-db89-4643-96ab-a1c2e32cfee1"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("796f9cd8-7132-45e4-b4ce-afdce4476e22"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("877293ba-4650-488a-a0a7-1eff8f5d02f5"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("99c0cddf-f6e8-4bba-a86f-33952523d0bb"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("9e3526b7-d00c-4bd3-b05f-7d15ece9d21b"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("b37367e7-6fed-454b-95c5-646e5d755130"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("b40abf4f-0a67-4a68-b39d-98ce2431efae"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("cfddadab-f4b8-4fbd-945c-4faef19ef2e4"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("d81a3e8a-0a9c-47fd-b568-5cbc2eef9f17"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("e2aeb96b-6937-42ac-8e67-37fe06876a9e"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("f07d963f-272a-4636-a7e5-ee6af713bfa5"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("f7143b02-3df5-4f28-95a5-6946ddfade4f"));

            migrationBuilder.DeleteData(
                table: "employees",
                keyColumn: "id",
                keyValue: new Guid("fe3538ee-640b-44dc-895e-e859eec2d2c7"));
        }
    }
}
