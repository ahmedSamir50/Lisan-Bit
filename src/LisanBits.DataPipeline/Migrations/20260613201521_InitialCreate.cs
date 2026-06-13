using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LisanBits.DataPipeline.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrawledUrlQueue",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DataSourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    DiscoveredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrawledUrlQueue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataSourceConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", nullable: false),
                    TargetXPath = table.Column<string>(type: "TEXT", nullable: false),
                    LinkXPath = table.Column<string>(type: "TEXT", nullable: true),
                    DiscoveryXPath = table.Column<string>(type: "TEXT", nullable: true),
                    PaginationParam = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSourceConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LexiconEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Word = table.Column<string>(type: "TEXT", nullable: false),
                    Root = table.Column<string>(type: "TEXT", nullable: false),
                    Definition = table.Column<string>(type: "TEXT", nullable: false),
                    Synonyms = table.Column<string>(type: "TEXT", nullable: false),
                    Antonyms = table.Column<string>(type: "TEXT", nullable: false),
                    Plurals = table.Column<string>(type: "TEXT", nullable: false),
                    SourceBook = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LexiconEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedUniversalData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RawDataId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessedText = table.Column<string>(type: "TEXT", nullable: false),
                    RootSequence = table.Column<string>(type: "TEXT", nullable: false),
                    PosSequence = table.Column<string>(type: "TEXT", nullable: false),
                    ContextVector = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedUniversalData", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawUniversalData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    SubContextPath = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    TextContent = table.Column<string>(type: "TEXT", nullable: false),
                    SentenceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ScrapedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawUniversalData", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScrapeJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceName = table.Column<string>(type: "TEXT", nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", nullable: false),
                    LastProcessedIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScrapeJobs", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "DataSourceConfigs",
                columns: new[] { "Id", "BaseUrl", "Category", "DiscoveryXPath", "IsActive", "LinkXPath", "Name", "PaginationParam", "TargetXPath" },
                values: new object[,]
                {
                    { 1, "https://shamela.ws/book/1687", "Linguistics", null, true, null, "Shamela (Lexicon)", "/", "//div[contains(@class, 'nass')]" },
                    { 2, "https://sunnah.com/", "Religion", "//a[starts-with(@href, '/') and string-length(@href) > 1 and not(contains(@href, '#')) and not(contains(@href, 'about')) and not(contains(@href, 'contact')) and not(contains(@href, 'help'))]", true, null, "Sunnah.com (All Books)", "", "//div[contains(@class, 'arabic_hadith_full')]" },
                    { 8, "file:///d:/A_S/LisanBitModel/quran-uthmani.xml", "Religion", null, true, null, "Tanzil Quran", "", "//aya" },
                    { 9, "https://ar.wikipedia.org/wiki/تصنيف:علم", "Science", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Science)", "", "//div[@id='mw-content-text']//p" },
                    { 10, "https://academy.hsoub.com/", "Science", "//h4[contains(@class, 'ipsDataItem_title')]//a | //a[contains(@href, 'page/')]", true, "//h4[contains(@class, 'ipsDataItem_title')]//a", "Hsoub Academy (All)", "", "//div[contains(@class, 'ipsType_normal')]" },
                    { 15, "https://altibbi.com/مصطلحات-طبية", "Medical", "//a[contains(@href, '/مصطلحات-طبية/')]", true, "//a[contains(@href, '/مصطلحات-طبية/')]", "Altibbi (Medical)", "?page=", "//div[contains(@class, 'definition-content')]" },
                    { 16, "https://ar.wikipedia.org/wiki/تصنيف:طب", "Medical", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Medicine)", "", "//div[@id='mw-content-text']//p" },
                    { 17, "https://sa.investing.com/education/terms", "Finance", "//div[contains(@class, 'textDiv')]//a | //div[contains(@class, 'midDiv')]//a | //div[contains(@class, 'sideDiv')]//a", true, "//div[contains(@class, 'textDiv')]//a | //div[contains(@class, 'midDiv')]//a | //div[contains(@class, 'sideDiv')]//a", "Investing.com", "?page=", "//div[contains(@class, 'WYSIWYG')]" },
                    { 18, "https://www.aleqt.com/", "Finance", "//h3//a | //h2//a | //div[contains(@class, 'flex-col')]//a", true, "//h3//a | //h2//a | //div[contains(@class, 'flex-col')]//a", "Al-Eqtisad", "?page=", "//div[contains(@class, 'article-content')]/div[not(@class) or @class='']" },
                    { 19, "https://www.hindawi.org/books/categories/novels/", "Literature", "//div[contains(@class, 'book-list')]//a", false, "//div[contains(@class, 'book-list')]//a", "Hindawi (Novels)", "", "//div[@id='book-content']" },
                    { 20, "https://raw.githubusercontent.com/Mostafanofal453/2.5-Million-Rows-Egyptian-Datasets-Collection/main/dataset.csv", "Slang", null, false, null, "Egyptian Slang (Nofal)", "", "CSV" },
                    { 21, "https://www.kooora.com/", "Sports", "//td[contains(@class, 'news_title')]//a | //a[contains(@href, '?n=')] | //a[contains(@href, '%D8%A3%D8%AE%D8%A8%D8%A7%D8%B1') or contains(@href, '/أخبار/')]", true, "//td[contains(@class, 'news_title')]//a | //a[contains(@href, '?n=')] | //a[contains(@href, '%D8%A3%D8%AE%D8%A8%D8%A7%D8%B1') or contains(@href, '/أخبار/')]", "Kooora", "", "//div[contains(@id, 'article_body')]" },
                    { 22, "https://www.aljazeera.net/", "News", "//a[starts-with(@href, '/') and (contains(@href, '/news/') or contains(@href, '/sport/'))] | //a[contains(@href, 'aljazeera.net/news/') or contains(@href, 'aljazeera.net/sport/')]", false, "//a[starts-with(@href, '/') and (contains(@href, '/news/') or contains(@href, '/sport/'))] | //a[contains(@href, 'aljazeera.net/news/') or contains(@href, 'aljazeera.net/sport/')]", "Al Jazeera", "", "//div[contains(@class, 'wysiwyg')]" },
                    { 23, "https://www.skynewsarabia.com/", "News", "//a[contains(@class, 'article-title') or contains(@href, '/world/') or contains(@href, '/middle-east/') or contains(@href, '/live-story/')]", false, "//a[contains(@class, 'article-title') or contains(@href, '/world/') or contains(@href, '/middle-east/') or contains(@href, '/live-story/')]", "Sky News Arabia", "", "//div[contains(@class, 'article-body')]" },
                    { 24, "https://ar.wikihow.com/الصفحة-الرئيسية", "DailyLife", "//a[contains(@class, 'related-article')]", false, "//a[contains(@class, 'related-article')]", "WikiHow Arabic", "", "//div[contains(@class, 'step')]" },
                    { 25, "https://www.youm7.com/", "News", "//h3//a | //h2//a | //a[contains(@href, '/story/') or contains(@href, '/news/')]", true, "//h3//a | //h2//a | //a[contains(@href, '/story/') or contains(@href, '/news/')]", "Youm7 (News)", "", "//div[contains(@class, 'article-text')]//p | //div[@id='articleBody']//p" },
                    { 26, "https://www.masrawy.com/news", "News", "//a[contains(@href, '/news/') or contains(@href, '/article/')]", true, "//a[contains(@href, '/news/') or contains(@href, '/article/')]", "Masrawy (News)", "", "//div[contains(@class, 'article-body')]//p | //div[contains(@class, 'news-body')]//p" },
                    { 27, "https://ar.wikipedia.org/wiki/تصنيف:أدب", "Literature", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Literature)", "", "//div[@id='mw-content-text']//p" },
                    { 28, "https://ar.wikipedia.org/wiki/تصنيف:حياة_يومية", "DailyLife", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (DailyLife)", "", "//div[@id='mw-content-text']//p" },
                    { 29, "file:///D:/A_S/nofal_slang.csv", "Slang", null, true, null, "Nofal Slang (Local CSV)", "", "CSV_FILE" },
                    { 30, "file:///D:/A_S/arb-egy-cmp-corpus_corrected.json", "Slang", null, false, null, "ARB-EGY-CMP (Local JSON)", "", "JSONL_TEXT_FILE" },
                    { 31, "https://shamela.ws/book/7283", "Linguistics", null, true, null, "Shamela (Lexicon)_Moheet", "/", "//div[contains(@class, 'nass')]" },
                    { 32, "https://shamela.ws/book/7030", "Linguistics", null, true, null, "Shamela (Lexicon)_Tag_Aroos", "/", "//div[contains(@class, 'nass')]" },
                    { 33, "https://shamela.ws/book/7028", "Linguistics", null, true, null, "Shamela (Lexicon)_Wasset", "/", "//div[contains(@class, 'nass')]" },
                    { 34, "https://shamela.ws/book/1682", "Linguistics", null, true, null, "Shamela (Lexicon)_AlAin", "/", "//div[contains(@class, 'nass')]" },
                    { 35, "https://shamela.ws/book/150964", "Linguistics", null, true, null, "Shamela (Lexicon)_Taimoor", "/", "//div[contains(@class, 'nass')]" },
                    { 36, "https://ar.wikipedia.org/wiki/تصنيف:فيزياء", "Science", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Physics)", "", "//div[@id='mw-content-text']//p" },
                    { 37, "https://ar.wikipedia.org/wiki/تصنيف:كيمياء", "Science", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Chemistry)", "", "//div[@id='mw-content-text']//p" },
                    { 38, "https://ar.wikipedia.org/wiki/تصنيف:أحياء", "Science", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Biology)", "", "//div[@id='mw-content-text']//p" },
                    { 39, "https://ar.wikipedia.org/wiki/تصنيف:رياضيات", "Science", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Mathematics)", "", "//div[@id='mw-content-text']//p" },
                    { 40, "https://ar.wikipedia.org/wiki/تصنيف:أمراض_القلب", "Medical", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Cardiology)", "", "//div[@id='mw-content-text']//p" },
                    { 41, "https://ar.wikipedia.org/wiki/تصنيف:الجهاز_العصبي", "Medical", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Neurology)", "", "//div[@id='mw-content-text']//p" },
                    { 42, "https://ar.wikipedia.org/wiki/تصنيف:اقتصاد", "Finance", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Economics)", "", "//div[@id='mw-content-text']//p" },
                    { 43, "https://ar.wikipedia.org/wiki/تصنيف:شعر_عربي", "Literature", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Poetry)", "", "//div[@id='mw-content-text']//p" },
                    { 44, "https://ar.wikipedia.org/wiki/تصنيف:طعام_وشراب", "DailyLife", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Food & DailyLife)", "", "//div[@id='mw-content-text']//p" },
                    { 45, "https://ar.wikipedia.org/wiki/تصنيف:فقه_إسلامي", "Religion", "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", true, "//div[@id='mw-pages']//a", "Wikipedia (Islamic Fiqh)", "", "//div[@id='mw-content-text']//p" },
                    { 46, "https://shamela.ws/book/356", "Linguistics", null, true, null, "Shamela (Grammar)_Alfiyyat", "/", "//div[contains(@class, 'nass')]" },
                    { 47, "https://shamela.ws/book/11376", "Linguistics", null, true, null, "Shamela (Grammar)_Qatr_Nada", "/", "//div[contains(@class, 'nass')]" },
                    { 48, "https://shamela.ws/book/6969", "Linguistics", null, true, null, "Shamela (Grammar)_Shudhur_Dhahab", "/", "//div[contains(@class, 'nass')]" },
                    { 49, "https://shamela.ws/book/23018", "Linguistics", null, true, null, "Shamela (Grammar)_Sibawayh_Kitab", "/", "//div[contains(@class, 'nass')]" },
                    { 50, "https://camel.abudhabi.nyu.edu/madar-parallel-corpus/MADAR.Parallel-Corpora-Public-Version1.1-25MAR2021.zip", "Dialect", null, true, null, "MADAR Parallel Corpus (Local TSV)", "", "TSV_FILE" },
                    { 51, "https://camel.abudhabi.nyu.edu/madar-coda-corpus/madar.coda-corpus.zip", "Dialect", null, true, null, "MADAR CODA Corpus (Local TSV)", "", "TSV_FILE" },
                    { 52, "https://camel.abudhabi.nyu.edu/madar-shared-task-2019/MADAR-SHARED-TASK-final-release-25Jul2019.zip", "Dialect", null, true, null, "MADAR Shared Task 2019 (Local TSV)", "", "TSV_FILE" },
                    { 53, "https://huggingface.co/datasets/oscar-corpus/OSCAR-2201/resolve/main/data/ar_meta_part_1.jsonl.zst", "General", null, false, null, "OSCAR Arabic", "", "JSONL_ZST_FILE" },
                    { 54, "https://data.statmt.org/cc-100/ar.txt.xz", "General", null, false, null, "CC-100 Arabic", "", "XZ_TEXT_FILE" },
                    { 55, "https://object.pouta.csc.fi/OPUS-MultiUN/v1/moses/ar-en.txt.zip", "General", null, false, null, "OPUS Parallel Corpus", "", "MOSES_ZIP_FILE" },
                    { 56, "https://object.pouta.csc.fi/OPUS-OpenSubtitles/v2018/mono/ar.txt.gz", "Dialect", null, false, null, "OpenSubtitles (Arabic)", "", "GZ_TEXT_FILE" },
                    { 57, "https://raw.githubusercontent.com/mustafa0x/quran-morphology/master/quran-morphology.txt", "Religion", null, true, null, "Quranic Arabic Corpus (Morphology)", "", "TSV_FILE" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrawledUrlQueue_DataSourceId_Url",
                table: "CrawledUrlQueue",
                columns: new[] { "DataSourceId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LexiconEntries_Root",
                table: "LexiconEntries",
                column: "Root");

            migrationBuilder.CreateIndex(
                name: "IX_LexiconEntries_Word",
                table: "LexiconEntries",
                column: "Word");

            migrationBuilder.CreateIndex(
                name: "IX_ScrapeJobs_SourceName_TargetId",
                table: "ScrapeJobs",
                columns: new[] { "SourceName", "TargetId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrawledUrlQueue");

            migrationBuilder.DropTable(
                name: "DataSourceConfigs");

            migrationBuilder.DropTable(
                name: "LexiconEntries");

            migrationBuilder.DropTable(
                name: "ProcessedUniversalData");

            migrationBuilder.DropTable(
                name: "RawUniversalData");

            migrationBuilder.DropTable(
                name: "ScrapeJobs");
        }
    }
}
