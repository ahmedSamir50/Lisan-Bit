using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LisanBits.DataPipeline.Migrations
{
    /// <inheritdoc />
    public partial class AddSubContextPathToRaw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubContextPath",
                table: "RawUniversalData",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubContextPath",
                table: "RawUniversalData");
        }
    }
}
