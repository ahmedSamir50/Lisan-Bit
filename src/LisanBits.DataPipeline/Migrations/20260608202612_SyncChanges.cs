using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LisanBits.DataPipeline.Migrations
{
    /// <inheritdoc />
    public partial class SyncChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "DataSourceConfigs",
                keyColumn: "Id",
                keyValue: 29,
                column: "IsActive",
                value: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "DataSourceConfigs",
                keyColumn: "Id",
                keyValue: 29,
                column: "IsActive",
                value: false);
        }
    }
}
