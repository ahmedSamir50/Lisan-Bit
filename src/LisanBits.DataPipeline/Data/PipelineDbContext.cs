using LisanBits.DataPipeline.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace LisanBits.DataPipeline.Data;

public class PipelineDbContext : DbContext
{
    public PipelineDbContext(DbContextOptions<PipelineDbContext> options) : base(options)
    {
    }

    public DbSet<ScrapeJob> ScrapeJobs => Set<ScrapeJob>();
    public DbSet<CrawledUrl> CrawledUrlQueue => Set<CrawledUrl>();
    public DbSet<RawUniversalData> RawUniversalData => Set<RawUniversalData>();
    public DbSet<ProcessedUniversalData> ProcessedUniversalData => Set<ProcessedUniversalData>();
    public DbSet<DataSourceConfig> DataSourceConfigs => Set<DataSourceConfig>();
    public DbSet<LexiconEntry> LexiconEntries => Set<LexiconEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LexiconEntry>()
            .HasIndex(l => l.Word);

        modelBuilder.Entity<LexiconEntry>()
            .HasIndex(l => l.Root);

        modelBuilder.Entity<ScrapeJob>()
            .HasIndex(j => new { j.SourceName, j.TargetId })
            .IsUnique();
            
        modelBuilder.Entity<CrawledUrl>()
            .HasIndex(c => new { c.DataSourceId, c.Url })
            .IsUnique();

        // Seed the Universal Configuration with N-Depth Crawling
        modelBuilder.Entity<DataSourceConfig>().HasData(
            // lenguisitics
            new DataSourceConfig { Id = 1, Name = "Shamela (Lexicon)", Category = "Linguistics", BaseUrl = "https://shamela.ws/book/1687", TargetXPath = "//div[contains(@class, 'nass')]", PaginationParam = "/", LinkXPath = null, DiscoveryXPath = null, IsActive = true },
            // Religion
            new DataSourceConfig { Id = 2, Name = "Sunnah.com (All Books)", Category = "Religion", BaseUrl = "https://sunnah.com/", TargetXPath = "//div[contains(@class, 'arabic_hadith_full')]", PaginationParam = "", LinkXPath = null, DiscoveryXPath = "//a[starts-with(@href, '/') and string-length(@href) > 1 and not(contains(@href, '#')) and not(contains(@href, 'about')) and not(contains(@href, 'contact')) and not(contains(@href, 'help'))]", IsActive = true },
            new DataSourceConfig { Id = 8, Name = "Tanzil Quran", Category = "Religion", BaseUrl = "file:///d:/A_S/LisanBitModel/quran-uthmani.xml", TargetXPath = "//aya", PaginationParam = "", LinkXPath = null, DiscoveryXPath = null, IsActive = true },
            
            // Science / General
            new DataSourceConfig { Id = 9, Name = "Wikipedia (Science)", Category = "Science", BaseUrl = "https://ar.wikipedia.org/wiki/تصنيف:علم", TargetXPath = "//div[@id='mw-content-text']//p", PaginationParam = "", LinkXPath = "//div[@id='mw-pages']//a", DiscoveryXPath = "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", IsActive = true },
            new DataSourceConfig { Id = 10, Name = "Hsoub Academy (All)", Category = "Science", BaseUrl = "https://academy.hsoub.com/", TargetXPath = "//div[contains(@class, 'ipsType_normal')]", PaginationParam = "", LinkXPath = "//h4[contains(@class, 'ipsDataItem_title')]//a", DiscoveryXPath = "//h4[contains(@class, 'ipsDataItem_title')]//a | //a[contains(@href, 'page/')]", IsActive = true },
            
            // Medical
            new DataSourceConfig { Id = 15, Name = "Altibbi (Medical)", Category = "Medical", BaseUrl = "https://altibbi.com/مصطلحات-طبية", TargetXPath = "//div[contains(@class, 'definition-content')]", PaginationParam = "?page=", LinkXPath = "//a[contains(@href, '/مصطلحات-طبية/')]", DiscoveryXPath = "//a[contains(@href, '/مصطلحات-طبية/')]", IsActive = true },
            new DataSourceConfig { Id = 16, Name = "Wikipedia (Medicine)", Category = "Medical", BaseUrl = "https://ar.wikipedia.org/wiki/تصنيف:طب", TargetXPath = "//div[@id='mw-content-text']//p", PaginationParam = "", LinkXPath = "//div[@id='mw-pages']//a", DiscoveryXPath = "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", IsActive = true },
            
            // Finance
            new DataSourceConfig { Id = 17, Name = "Investing.com", Category = "Finance", BaseUrl = "https://sa.investing.com/education/terms", TargetXPath = "//div[contains(@class, 'WYSIWYG')]", PaginationParam = "?page=", LinkXPath = "//div[contains(@class, 'textDiv')]//a | //div[contains(@class, 'midDiv')]//a | //div[contains(@class, 'sideDiv')]//a", DiscoveryXPath = "//div[contains(@class, 'textDiv')]//a | //div[contains(@class, 'midDiv')]//a | //div[contains(@class, 'sideDiv')]//a", IsActive = true },
            new DataSourceConfig { Id = 18, Name = "Al-Eqtisad", Category = "Finance", BaseUrl = "https://www.aleqt.com/", TargetXPath = "//div[contains(@class, 'article-content')]/div[not(@class) or @class='']", PaginationParam = "?page=", LinkXPath = "//h3//a | //h2//a | //div[contains(@class, 'flex-col')]//a", DiscoveryXPath = "//h3//a | //h2//a | //div[contains(@class, 'flex-col')]//a", IsActive = true },
            
            // Literature / Sports / Other
            new DataSourceConfig { Id = 19, Name = "Hindawi (Novels)", Category = "Literature", BaseUrl = "https://www.hindawi.org/books/categories/novels/", TargetXPath = "//div[@id='book-content']", PaginationParam = "", LinkXPath = "//div[contains(@class, 'book-list')]//a", DiscoveryXPath = "//div[contains(@class, 'book-list')]//a", IsActive = false },
            new DataSourceConfig { Id = 20, Name = "Egyptian Slang (Nofal)", Category = "Slang", BaseUrl = "https://raw.githubusercontent.com/Mostafanofal453/2.5-Million-Rows-Egyptian-Datasets-Collection/main/dataset.csv", TargetXPath = "CSV", PaginationParam = "", LinkXPath = null, DiscoveryXPath = null, IsActive = false },
            new DataSourceConfig { Id = 21, Name = "Kooora", Category = "Sports", BaseUrl = "https://www.kooora.com/", TargetXPath = "//div[contains(@id, 'article_body')]", PaginationParam = "", LinkXPath = "//td[contains(@class, 'news_title')]//a | //a[contains(@href, '?n=')] | //a[contains(@href, '%D8%A3%D8%AE%D8%A8%D8%A7%D8%B1') or contains(@href, '/أخبار/')]", DiscoveryXPath = "//td[contains(@class, 'news_title')]//a | //a[contains(@href, '?n=')] | //a[contains(@href, '%D8%A3%D8%AE%D8%A8%D8%A7%D8%B1') or contains(@href, '/أخبار/')]", IsActive = true },
            new DataSourceConfig { Id = 22, Name = "Al Jazeera", Category = "News", BaseUrl = "https://www.aljazeera.net/", TargetXPath = "//div[contains(@class, 'wysiwyg')]", PaginationParam = "", LinkXPath = "//a[starts-with(@href, '/') and (contains(@href, '/news/') or contains(@href, '/sport/'))] | //a[contains(@href, 'aljazeera.net/news/') or contains(@href, 'aljazeera.net/sport/')]", DiscoveryXPath = "//a[starts-with(@href, '/') and (contains(@href, '/news/') or contains(@href, '/sport/'))] | //a[contains(@href, 'aljazeera.net/news/') or contains(@href, 'aljazeera.net/sport/')]", IsActive = false },
            new DataSourceConfig { Id = 23, Name = "Sky News Arabia", Category = "News", BaseUrl = "https://www.skynewsarabia.com/", TargetXPath = "//div[contains(@class, 'article-body')]", PaginationParam = "", LinkXPath = "//a[contains(@class, 'article-title') or contains(@href, '/world/') or contains(@href, '/middle-east/') or contains(@href, '/live-story/')]", DiscoveryXPath = "//a[contains(@class, 'article-title') or contains(@href, '/world/') or contains(@href, '/middle-east/') or contains(@href, '/live-story/')]", IsActive = false },
            new DataSourceConfig { Id = 24, Name = "WikiHow Arabic", Category = "DailyLife", BaseUrl = "https://ar.wikihow.com/الصفحة-الرئيسية", TargetXPath = "//div[contains(@class, 'step')]", PaginationParam = "", LinkXPath = "//a[contains(@class, 'related-article')]", DiscoveryXPath = "//a[contains(@class, 'related-article')]", IsActive = false },

            // === NEW SOURCES ===
            // News (Egyptian - less CDN-blocked than Al Jazeera/SkyNews)
            new DataSourceConfig { Id = 25, Name = "Youm7 (News)", Category = "News", BaseUrl = "https://www.youm7.com/", TargetXPath = "//div[contains(@class, 'article-text')]//p | //div[@id='articleBody']//p", PaginationParam = "", LinkXPath = "//h3//a | //h2//a | //a[contains(@href, '/story/') or contains(@href, '/news/')]", DiscoveryXPath = "//h3//a | //h2//a | //a[contains(@href, '/story/') or contains(@href, '/news/')]", IsActive = true },
            new DataSourceConfig { Id = 26, Name = "Masrawy (News)", Category = "News", BaseUrl = "https://www.masrawy.com/news", TargetXPath = "//div[contains(@class, 'article-body')]//p | //div[contains(@class, 'news-body')]//p", PaginationParam = "", LinkXPath = "//a[contains(@href, '/news/') or contains(@href, '/article/')]", DiscoveryXPath = "//a[contains(@href, '/news/') or contains(@href, '/article/')]", IsActive = true },

            // Literature (Wikipedia Arabic Literature category - proven to work)
            new DataSourceConfig { Id = 27, Name = "Wikipedia (Literature)", Category = "Literature", BaseUrl = "https://ar.wikipedia.org/wiki/تصنيف:أدب", TargetXPath = "//div[@id='mw-content-text']//p", PaginationParam = "", LinkXPath = "//div[@id='mw-pages']//a", DiscoveryXPath = "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", IsActive = true },

            // DailyLife (Wikipedia Arabic DailyLife category)
            new DataSourceConfig { Id = 28, Name = "Wikipedia (DailyLife)", Category = "DailyLife", BaseUrl = "https://ar.wikipedia.org/wiki/تصنيف:حياة_يومية", TargetXPath = "//div[@id='mw-content-text']//p", PaginationParam = "", LinkXPath = "//div[@id='mw-pages']//a", DiscoveryXPath = "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", IsActive = true },

            // Nofal Slang dataset - local CSV file (inactive by default; user enables after downloading)
            // Download: curl -L -o D:\A_S\nofal_slang.csv <kaggle-url> then unzip; set IsActive=true via dashboard
            new DataSourceConfig { Id = 29, Name = "Nofal Slang (Local CSV)", Category = "Slang", BaseUrl = "file:///D:/A_S/nofal_slang.csv", TargetXPath = "CSV_FILE", PaginationParam = "", LinkXPath = null, DiscoveryXPath = null, IsActive = true },

            // ARB-EGY-CMP corpus - local JSONL after decode fix (inactive by default)
            new DataSourceConfig { Id = 30, Name = "ARB-EGY-CMP (Local JSON)", Category = "Slang", BaseUrl = "file:///D:/A_S/arb-egy-cmp-corpus_corrected.json", TargetXPath = "JSONL_TEXT_FILE", PaginationParam = "", LinkXPath = null, DiscoveryXPath = null, IsActive = false },
         // lenguisitics
            new DataSourceConfig { Id = 31, Name = "Shamela (Lexicon)_Moheet", Category = "Linguistics", BaseUrl = "https://shamela.ws/book/7283", TargetXPath = "//div[contains(@class, 'nass')]", PaginationParam = "/", LinkXPath = null, DiscoveryXPath = null, IsActive = true },
            new DataSourceConfig { Id = 32, Name = "Shamela (Lexicon)_Tag_Aroos", Category = "Linguistics", BaseUrl = "https://shamela.ws/book/7030", TargetXPath = "//div[contains(@class, 'nass')]", PaginationParam = "/", LinkXPath = null, DiscoveryXPath = null, IsActive = true },
            new DataSourceConfig { Id = 33, Name = "Shamela (Lexicon)_Wasset", Category = "Linguistics", BaseUrl = "https://shamela.ws/book/7028", TargetXPath = "//div[contains(@class, 'nass')]", PaginationParam = "/", LinkXPath = null, DiscoveryXPath = null, IsActive = true },
            new DataSourceConfig { Id = 34, Name = "Shamela (Lexicon)_AlAin", Category = "Linguistics", BaseUrl = "https://shamela.ws/book/1682", TargetXPath = "//div[contains(@class, 'nass')]", PaginationParam = "/", LinkXPath = null, DiscoveryXPath = null, IsActive = true },
            new DataSourceConfig { Id = 35, Name = "Shamela (Lexicon)_Taimoor", Category = "Linguistics", BaseUrl = "https://shamela.ws/book/150964", TargetXPath = "//div[contains(@class, 'nass')]", PaginationParam = "/", LinkXPath = null, DiscoveryXPath = null, IsActive = true },

            // === GENUS-AWARE SUB-CATEGORY SOURCES (IDs 36–45) ===
            // These target specific Arabic Wikipedia category pages to enrich under-represented domains.
            // Category footer (#mw-normal-catlinks) is resolved to taxonomy leaf paths via WikiCategoryResolver.
            new DataSourceConfig { Id = 36, Name = "Wikipedia (Physics)", Category = "Science", BaseUrl = "https://ar.wikipedia.org/wiki/تصنيف:فيزياء", TargetXPath = "//div[@id='mw-content-text']//p", PaginationParam = "", LinkXPath = "//div[@id='mw-pages']//a", DiscoveryXPath = "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", IsActive = true },
            new DataSourceConfig { Id = 37, Name = "Wikipedia (Chemistry)", Category = "Science", BaseUrl = "https://ar.wikipedia.org/wiki/تصنيف:كيمياء", TargetXPath = "//div[@id='mw-content-text']//p", PaginationParam = "", LinkXPath = "//div[@id='mw-pages']//a", DiscoveryXPath = "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", IsActive = true },
            new DataSourceConfig { Id = 38, Name = "Wikipedia (Biology)", Category = "Science", BaseUrl = "https://ar.wikipedia.org/wiki/تصنيف:أحياء", TargetXPath = "//div[@id='mw-content-text']//p", PaginationParam = "", LinkXPath = "//div[@id='mw-pages']//a", DiscoveryXPath = "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", IsActive = true },
            new DataSourceConfig { Id = 39, Name = "Wikipedia (Mathematics)", Category = "Science", BaseUrl = "https://ar.wikipedia.org/wiki/تصنيف:رياضيات", TargetXPath = "//div[@id='mw-content-text']//p", PaginationParam = "", LinkXPath = "//div[@id='mw-pages']//a", DiscoveryXPath = "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", IsActive = true },
            new DataSourceConfig { Id = 40, Name = "Wikipedia (Cardiology)", Category = "Medical", BaseUrl = "https://ar.wikipedia.org/wiki/تصنيف:أمراض_القلب", TargetXPath = "//div[@id='mw-content-text']//p", PaginationParam = "", LinkXPath = "//div[@id='mw-pages']//a", DiscoveryXPath = "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", IsActive = true },
            new DataSourceConfig { Id = 41, Name = "Wikipedia (Neurology)", Category = "Medical", BaseUrl = "https://ar.wikipedia.org/wiki/تصنيف:الجهاز_العصبي", TargetXPath = "//div[@id='mw-content-text']//p", PaginationParam = "", LinkXPath = "//div[@id='mw-pages']//a", DiscoveryXPath = "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", IsActive = true },
            new DataSourceConfig { Id = 42, Name = "Wikipedia (Economics)", Category = "Finance", BaseUrl = "https://ar.wikipedia.org/wiki/تصنيف:اقتصاد", TargetXPath = "//div[@id='mw-content-text']//p", PaginationParam = "", LinkXPath = "//div[@id='mw-pages']//a", DiscoveryXPath = "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", IsActive = true },
            new DataSourceConfig { Id = 43, Name = "Wikipedia (Poetry)", Category = "Literature", BaseUrl = "https://ar.wikipedia.org/wiki/تصنيف:شعر_عربي", TargetXPath = "//div[@id='mw-content-text']//p", PaginationParam = "", LinkXPath = "//div[@id='mw-pages']//a", DiscoveryXPath = "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", IsActive = true },
            new DataSourceConfig { Id = 44, Name = "Wikipedia (Food & DailyLife)", Category = "DailyLife", BaseUrl = "https://ar.wikipedia.org/wiki/تصنيف:طعام_وشراب", TargetXPath = "//div[@id='mw-content-text']//p", PaginationParam = "", LinkXPath = "//div[@id='mw-pages']//a", DiscoveryXPath = "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", IsActive = true },
            new DataSourceConfig { Id = 45, Name = "Wikipedia (Islamic Fiqh)", Category = "Religion", BaseUrl = "https://ar.wikipedia.org/wiki/تصنيف:فقه_إسلامي", TargetXPath = "//div[@id='mw-content-text']//p", PaginationParam = "", LinkXPath = "//div[@id='mw-pages']//a", DiscoveryXPath = "//div[@id='mw-subcategories']//a | //div[@id='mw-pages']//a", IsActive = true },
            
            // Shamela Grammar References (Task 2.2 in the plan)
            new DataSourceConfig { Id = 46, Name = "Shamela (Grammar)_Alfiyyat", Category = "Linguistics", BaseUrl = "https://shamela.ws/book/356", TargetXPath = "//div[contains(@class, 'nass')]", PaginationParam = "/", LinkXPath = null, DiscoveryXPath = null, IsActive = true },
            new DataSourceConfig { Id = 47, Name = "Shamela (Grammar)_Qatr_Nada", Category = "Linguistics", BaseUrl = "https://shamela.ws/book/11376", TargetXPath = "//div[contains(@class, 'nass')]", PaginationParam = "/", LinkXPath = null, DiscoveryXPath = null, IsActive = true },
            new DataSourceConfig { Id = 48, Name = "Shamela (Grammar)_Shudhur_Dhahab", Category = "Linguistics", BaseUrl = "https://shamela.ws/book/6969", TargetXPath = "//div[contains(@class, 'nass')]", PaginationParam = "/", LinkXPath = null, DiscoveryXPath = null, IsActive = true },
            new DataSourceConfig { Id = 49, Name = "Shamela (Grammar)_Sibawayh_Kitab", Category = "Linguistics", BaseUrl = "https://shamela.ws/book/23018", TargetXPath = "//div[contains(@class, 'nass')]", PaginationParam = "/", LinkXPath = null, DiscoveryXPath = null, IsActive = true },

            // MADAR Datasets (Dialect Support)
            new DataSourceConfig { Id = 50, Name = "MADAR Parallel Corpus (Local TSV)", Category = "Dialect", BaseUrl = "https://camel.abudhabi.nyu.edu/madar-parallel-corpus/MADAR.Parallel-Corpora-Public-Version1.1-25MAR2021.zip", TargetXPath = "TSV_FILE", PaginationParam = "", LinkXPath = null, DiscoveryXPath = null, IsActive = true },
            new DataSourceConfig { Id = 51, Name = "MADAR CODA Corpus (Local TSV)", Category = "Dialect", BaseUrl = "https://camel.abudhabi.nyu.edu/madar-coda-corpus/madar.coda-corpus.zip", TargetXPath = "TSV_FILE", PaginationParam = "", LinkXPath = null, DiscoveryXPath = null, IsActive = true },
            new DataSourceConfig { Id = 52, Name = "MADAR Shared Task 2019 (Local TSV)", Category = "Dialect", BaseUrl = "https://camel.abudhabi.nyu.edu/madar-shared-task-2019/MADAR-SHARED-TASK-final-release-25Jul2019.zip", TargetXPath = "TSV_FILE", PaginationParam = "", LinkXPath = null, DiscoveryXPath = null, IsActive = true }
            );
    }
}
