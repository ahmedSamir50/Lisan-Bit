using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LisanBits.DataPipeline.Migrations
{
    /// <inheritdoc />
    public partial class AddSubCategorySources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "DataSourceConfigs",
                columns: new[] { "Id", "BaseUrl", "Category", "DiscoveryXPath", "IsActive", "LinkXPath", "Name", "PaginationParam", "TargetXPath" },
                values: new object[,]
                {
                    { 36, "https://ar.wikipedia.org/wiki/تصنيف:فيزياء", "Science", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Physics)", "", "//div[@id='mw-content-text']//p" },
                    { 37, "https://ar.wikipedia.org/wiki/تصنيف:كيمياء", "Science", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Chemistry)", "", "//div[@id='mw-content-text']//p" },
                    { 38, "https://ar.wikipedia.org/wiki/تصنيف:أحياء", "Science", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Biology)", "", "//div[@id='mw-content-text']//p" },
                    { 39, "https://ar.wikipedia.org/wiki/تصنيف:رياضيات", "Science", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Mathematics)", "", "//div[@id='mw-content-text']//p" },
                    { 40, "https://ar.wikipedia.org/wiki/تصنيف:أمراض_القلب", "Medical", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Cardiology)", "", "//div[@id='mw-content-text']//p" },
                    { 41, "https://ar.wikipedia.org/wiki/تصنيف:الجهاز_العصبي", "Medical", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Neurology)", "", "//div[@id='mw-content-text']//p" },
                    { 42, "https://ar.wikipedia.org/wiki/تصنيف:اقتصاد", "Finance", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Economics)", "", "//div[@id='mw-content-text']//p" },
                    { 43, "https://ar.wikipedia.org/wiki/تصنيف:شعر_عربي", "Literature", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Poetry)", "", "//div[@id='mw-content-text']//p" },
                    { 44, "https://ar.wikipedia.org/wiki/تصنيف:طعام_وشراب", "DailyLife", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Food & DailyLife)", "", "//div[@id='mw-content-text']//p" },
                    { 45, "https://ar.wikipedia.org/wiki/تصنيف:فقه_إسلامي", "Religion", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Islamic Fiqh)", "", "//div[@id='mw-content-text']//p" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "DataSourceConfigs",
                keyColumn: "Id",
                keyValue: 36);

            migrationBuilder.DeleteData(
                table: "DataSourceConfigs",
                keyColumn: "Id",
                keyValue: 37);

            migrationBuilder.DeleteData(
                table: "DataSourceConfigs",
                keyColumn: "Id",
                keyValue: 38);

            migrationBuilder.DeleteData(
                table: "DataSourceConfigs",
                keyColumn: "Id",
                keyValue: 39);

            migrationBuilder.DeleteData(
                table: "DataSourceConfigs",
                keyColumn: "Id",
                keyValue: 40);

            migrationBuilder.DeleteData(
                table: "DataSourceConfigs",
                keyColumn: "Id",
                keyValue: 41);

            migrationBuilder.DeleteData(
                table: "DataSourceConfigs",
                keyColumn: "Id",
                keyValue: 42);

            migrationBuilder.DeleteData(
                table: "DataSourceConfigs",
                keyColumn: "Id",
                keyValue: 43);

            migrationBuilder.DeleteData(
                table: "DataSourceConfigs",
                keyColumn: "Id",
                keyValue: 44);

            migrationBuilder.DeleteData(
                table: "DataSourceConfigs",
                keyColumn: "Id",
                keyValue: 45);
        }
    }
}
