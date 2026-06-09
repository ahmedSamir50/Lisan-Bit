using System.Collections.Frozen;

namespace LisanBits.DataPipeline.Acquisition;

/// <summary>
/// Resolves Arabic Wikipedia category names (from #mw-normal-catlinks footer)
/// and Arabic news site section breadcrumbs to English taxonomy leaf paths.
/// Uses a longest-match rule: the most specific matching entry wins.
/// No raw Arabic strings are ever stored as ContextVector paths.
/// </summary>
public static class WikiCategoryResolver
{
    // -----------------------------------------------------------------------
    // Primary lookup: Arabic Wikipedia category slug → taxonomy leaf path.
    // Keys are URL-decoded category name segments (after "تصنيف:"), lowercased.
    // Longest match wins — more specific entries must come before generic ones.
    // -----------------------------------------------------------------------
    private static readonly FrozenDictionary<string, string> s_wikiMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Science / Medicine — specific first
            ["أمراض_القلب"]          = "Science/Medicine/Cardiology",
            ["الجهاز_العصبي"]        = "Science/Medicine/Neurology",
            ["جراحة_القلب"]          = "Science/Medicine/Cardiology",
            ["طب_الأعصاب"]           = "Science/Medicine/Neurology",
            ["طب"]                   = "Science/Medicine",
            ["طب_بشري"]              = "Science/Medicine",
            ["أحياء"]                = "Science/Biology",
            ["علم_الأحياء"]          = "Science/Biology",
            ["فيزياء"]               = "Science/Physics",
            ["كيمياء"]               = "Science/Chemistry",
            ["رياضيات"]              = "Science/Mathematics",
            ["علم_الفلك"]            = "Science/Astronomy",
            ["علوم"]                 = "Science",
            ["علم"]                  = "Science",

            // Finance / Economics
            ["اقتصاد_كلي"]           = "Finance/Economics/Macroeconomics",
            ["اقتصاد_جزئي"]          = "Finance/Economics/Microeconomics",
            ["اقتصاد"]               = "Finance/Economics",
            ["تمويل"]                = "Finance",
            ["بورصة"]                = "Finance/Stock",
            ["مصارف"]                = "Finance/Banking",
            ["بنوك"]                = "Finance/Banking",

            // Religion / Islam — specific first
            ["القرآن_الكريم"]        = "Religion/Islam/Quran",
            ["القرآن"]        = "Religion/Islam/Quran",
            ["تفسير_القرآن"]         = "Religion/Islam/Quran/Tafseer",
            ["فقه_إسلامي"]           = "Religion/Islam/Fiqh",
            ["عقيدة_إسلامية"]        = "Religion/Islam/Aqeedah",
            ["السيرة_النبوية"]        = "Religion/Islam/Seerah",
            ["حديث_نبوي"]            = "Religion/Islam/Hadith",
            ["إسلام"]                = "Religion/Islam",
            ["دين"]                  = "Religion",

            // Literature / Poetry — specific first
            ["شعر_عربي_كلاسيكي"]     = "Literature/Poetry/Classical",
            ["شعر_عربي"]             = "Literature/Poetry",
            ["شعر"]                  = "Literature/Poetry",
            ["روايات_عربية"]         = "Literature/Novels",
            ["أدب_عربي"]             = "Literature",
            ["أدب"]                  = "Literature",

            // Sports
            ["كرة_القدم"]            = "Sports/Football",
            ["رياضة"]                = "Sports",

            // DailyLife
            ["طعام_وشراب"]           = "DailyLife/Food",
            ["حياة_يومية"]           = "DailyLife",
            ["منزل"]                 = "DailyLife/Home",

            // Linguistics
            ["لغة_عربية"]            = "Linguistics/Classical",
            ["علم_اللغة"]            = "Linguistics",

            // News — generic fallback
            ["أخبار"]                = "News",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // -----------------------------------------------------------------------
    // Secondary lookup: Arabic news-site section breadcrumb labels.
    // Used when no Wikipedia category footer is present.
    // -----------------------------------------------------------------------
    private static readonly FrozenDictionary<string, string> s_newsSectionMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["رياضة"]          = "Sports",
            ["كرة القدم"]      = "Sports/Football",
            ["اقتصاد"]         = "Finance/Economics",
            ["أعمال"]          = "Finance",
            ["بورصة"]          = "Finance/Stock",
            ["صحة"]            = "Science/Medicine",
            ["طب"]             = "Science/Medicine",
            ["علوم وتكنولوجيا"] = "Science",
            ["تكنولوجيا"]      = "Science",
            ["منوعات"]         = "DailyLife",
            ["ترفيه"]          = "DailyLife",
            ["سياسة"]          = "News",
            ["أخبار"]          = "News",
            ["عالم"]           = "News",
            ["ثقافة"]          = "Literature",
            ["فنون"]           = "Literature",
            ["دين"]            = "Religion",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a Wikipedia article's category links (extracted from #mw-normal-catlinks)
    /// to the best-matching taxonomy leaf path.
    /// Returns null if no mapping is found — caller should fall back to DataSourceConfig.Category.
    /// </summary>
    /// <param name="categoryAnchors">
    /// The href/text values of category anchor tags scraped from #mw-normal-catlinks.
    /// Pass the raw Arabic text of the link (e.g. "أمراض القلب" or slug "أمراض_القلب").
    /// </param>
    public static string? ResolveFromWikiCategories(IEnumerable<string> categoryAnchors)
    {
        string? bestMatch = null;
        int bestSpecificity = 0; // higher slash-count = more specific

        foreach (var anchor in categoryAnchors)
        {
            // Normalize: replace spaces with underscores, trim
            var normalized = anchor.Trim().Replace(' ', '_');

            if (s_wikiMap.TryGetValue(normalized, out var path))
            {
                var specificity = path.Count(c => c == '/');
                if (specificity > bestSpecificity)
                {
                    bestMatch = path;
                    bestSpecificity = specificity;
                }
                else if (bestMatch is null)
                {
                    bestMatch = path;
                }
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// Resolves a news site's section breadcrumb label (e.g. "رياضة") to a taxonomy path.
    /// Returns null if no mapping is found.
    /// </summary>
    public static string? ResolveFromNewsSection(string? sectionLabel)
    {
        if (string.IsNullOrWhiteSpace(sectionLabel))
            return null;

        var key = sectionLabel.Trim();
        return s_newsSectionMap.TryGetValue(key, out var path) ? path : null;
    }

    /// <summary>
    /// Counts the number of unique roots in a Farasa root sequence string.
    /// Farasa returns roots as space-separated tokens.
    /// Returns true if the block meets the minimum quality threshold (≥7 unique roots).
    /// </summary>
    public static bool MeetsRootQualityThreshold(string? rootSequence, int minimumUniqueRoots = 7)
    {
        if (string.IsNullOrWhiteSpace(rootSequence))
            return false;

        // Farasa root sequences are space-separated. Filter out function-word
        // placeholders (single characters, punctuation, numbers).
        var uniqueRoots = rootSequence
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 2 && token.All(c => c >= '\u0600' && c <= '\u06FF'))
            .ToHashSet();

        return uniqueRoots.Count >= minimumUniqueRoots;
    }
}
