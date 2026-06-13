using System.Text;
using System.Text.RegularExpressions;
using Panlingo.LanguageIdentification.FastText;
using MinHashSharp;
using Shared.Extentions.StringExt;

namespace LisanBits.DataPipeline.Preprocessing;

public class DataCleaner
{
    private readonly ILogger<DataCleaner> _logger;
    private static readonly object _fastTextLock = new();
    private static FastTextDetector? _detector;
    private static bool _detectorInitializationFailed = false;

    private readonly object _minHashLock = new();
    private readonly MinHashLSH _lsh;
    private const int NumPermutations = 128;
    private const double SimilarityThreshold = 0.85;

    private const string DefaultModelDir = @"D:\A_S";
    private const string ModelFileName = "lid.176.ftz";
    private const string FastTextModelUrl = "https://dl.fbaipublicfiles.com/fasttext/supervised-models/lid.176.ftz";

    public DataCleaner(ILogger<DataCleaner> logger)
    {
        _logger = logger;
        
        // Initialize LSH index
        _lsh = new MinHashLSH(threshold: SimilarityThreshold, numPerm: NumPermutations);

        // Async trigger model prep so it doesn't block startup
        Task.Run(InitializeFastTextAsync);
    }

    private async Task InitializeFastTextAsync()
    {
        lock (_fastTextLock)
        {
            if (_detector != null || _detectorInitializationFailed) return;
        }

        string modelPath = Path.Combine(DefaultModelDir, ModelFileName);

        try
        {
            Directory.CreateDirectory(DefaultModelDir);

            if (!File.Exists(modelPath))
            {
                _logger.LogInformation("FastText model not found at {Path}. Downloading from {Url}...", modelPath, FastTextModelUrl);
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(5);
                var data = await client.GetByteArrayAsync(FastTextModelUrl);
                await File.WriteAllBytesAsync(modelPath, data);
                _logger.LogInformation("FastText model downloaded successfully.");
            }

            lock (_fastTextLock)
            {
                _detector = new FastTextDetector();
                _detector.LoadModel(modelPath);
                _logger.LogInformation("FastText detector initialized successfully with model: {Path}", modelPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize FastText language detector. Falling back to heuristic language matching.");
            lock (_fastTextLock)
            {
                _detectorInitializationFailed = true;
            }
        }
    }

    /// <summary>
    /// Processes the input text, normalizes it, and runs the language ID and deduplication gates.
    /// </summary>
    /// <param name="rawText">The raw input text to clean.</param>
    /// <param name="category">The data source category (e.g. Religion, Slang, Linguistics).</param>
    /// <param name="cleanedText">The resulting normalized, cleaned text.</param>
    /// <returns>True if the text passes all quality gates; false otherwise.</returns>
    public bool ProcessAndVerify(string rawText, string category, out string cleanedText)
    {
        cleanedText = string.Empty;
        if (string.IsNullOrWhiteSpace(rawText)) return false;

        // Gate 1: Length Filtering
        if (rawText.Length < 100 || rawText.Length > 100000)
        {
            // Quranic morphology annotations (which are processed through this route) can be short
            if (category != "Religion" && category != "Linguistics" || rawText.Length < 2)
            {
                return false;
            }
        }

        // Gate 2: Language Identification (Graceful degradation)
        bool isArabic = VerifyLanguage(rawText);
        if (!isArabic)
        {
            return false;
        }

        // Gate 3: Unicode Normalization
        cleanedText = StripTashkeelAndNormalize(rawText, category);
        if (string.IsNullOrWhiteSpace(cleanedText) || cleanedText.Length < 50)
        {
            if (category != "Religion" && category != "Linguistics")
            {
                return false;
            }
        }

        // Gate 4: Near-duplicate detection (MinHash + LSH)
        // Skip duplicate checking for fine-grained Quranic segment annotations
        if (category != "Religion" || rawText.Length > 50)
        {
            bool isDuplicate = CheckAndRegisterDuplicate(cleanedText);
            if (isDuplicate)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Processes lexicon entry text, normalizes it, and runs language ID and deduplication gates with a lower minimum length.
    /// </summary>
    public bool ProcessAndVerifyLexicon(string rawText, string category, out string cleanedText)
    {
        cleanedText = string.Empty;
        if (string.IsNullOrWhiteSpace(rawText)) return false;

        // Gate 1: Length Filtering for Lexicon is much laxer
        if (rawText.Length < 2 || rawText.Length > 100000)
        {
            return false;
        }

        // Gate 2: Language Identification (Graceful degradation)
        bool isArabic = VerifyLanguage(rawText);
        if (!isArabic)
        {
            return false;
        }

        // Gate 3: Unicode Normalization
        cleanedText = StripTashkeelAndNormalize(rawText, category);
        if (string.IsNullOrWhiteSpace(cleanedText))
        {
            return false;
        }

        // Gate 4: Near-duplicate detection (MinHash + LSH)
        bool isDuplicate = CheckAndRegisterDuplicate(cleanedText);
        if (isDuplicate)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Processes dialect/slang text, normalizes it, and runs quality gates with lower minimum length and dialect-aware language checks.
    /// </summary>
    public bool ProcessAndVerifyDialect(string rawText, string category, out string cleanedText)
    {
        cleanedText = string.Empty;
        if (string.IsNullOrWhiteSpace(rawText)) return false;

        // Gate 1: Length Filtering for Dialect
        if (rawText.Length < 5 || rawText.Length > 100000)
        {
            return false;
        }

        // Gate 2: Language Identification (Dialect-friendly: check FastText or Heuristic)
        bool isArabic = VerifyLanguage(rawText) || HeuristicIsArabic(rawText);
        if (!isArabic)
        {
            return false;
        }

        // Gate 3: Unicode Normalization
        cleanedText = StripTashkeelAndNormalize(rawText, category);
        if (string.IsNullOrWhiteSpace(cleanedText))
        {
            return false;
        }

        // Gate 4: Near-duplicate detection (MinHash + LSH)
        bool isDuplicate = CheckAndRegisterDuplicate(cleanedText);
        if (isDuplicate)
        {
            return false;
        }

        return true;
    }

    private bool VerifyLanguage(string text)
    {
        lock (_fastTextLock)
        {
            if (_detector != null)
            {
                try
                {
                    // FastText works best on lowercase, single-line trimmed text
                    var cleanForDetection = text.Replace("\r", " ").Replace("\n", " ").Trim();
                    var predictions = _detector.Predict(cleanForDetection, count: 3);
                    var top = predictions.FirstOrDefault();
                    if (top != null)
                    {
                        bool matchesArabic = top.Label.EndsWith("ar", StringComparison.OrdinalIgnoreCase);
                        if (matchesArabic && top.Probability > 0.9)
                        {
                            return true;
                        }
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "FastText prediction failed. Falling back to heuristic.");
                }
            }
        }

        // Graceful degradation fallback heuristic
        return HeuristicIsArabic(text);
    }

    private bool HeuristicIsArabic(string text)
    {
        int arabicChars = 0;
        int totalLetters = 0;
        
        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                totalLetters++;
                // Check if character lies in the Arabic unicode block (0x0600 - 0x06FF)
                if (c >= 0x0600 && c <= 0x06FF)
                {
                    arabicChars++;
                }
            }
        }

        if (totalLetters == 0) return false;
        double ratio = (double)arabicChars / totalLetters;
        return ratio > 0.7; // 70% threshold
    }

    public string StripTashkeelAndNormalize(string text, string category)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // Skip stripping diacritics and spelling normalizations for Religion (Quranic Arabic) and Linguistics (Lexicons)
        if (category == "Religion" || category == "Linguistics")
        {
            return text.Normalize(NormalizationForm.FormC); // Keep all diacritics and spelling exact (NFC)
        }

        // Use shared RemoveTashkeel extension
        var normalized = text.RemoveTashkeel();

        // Normalize Alefs
        normalized = normalized.Replace('أ', 'ا')
                               .Replace('إ', 'ا')
                               .Replace('آ', 'ا');
        // Normalize Teh Marbuta
        normalized = normalized.Replace('ة', 'ه');
        // Normalize Yeh
        normalized = normalized.Replace('ى', 'ي');

        return normalized;
    }

    private bool CheckAndRegisterDuplicate(string text)
    {
        // Simple word tokenization
        var tokens = text.Split(new[] { ' ', '\t', '\r', '\n', '，', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                         .Where(t => t.Length > 1)
                         .ToArray();

        if (tokens.Length < 5) return false; // Too short to deduplicate reliably

        var m = new MinHash(numPerm: NumPermutations).Update(tokens);

        lock (_minHashLock)
        {
            var matches = _lsh.Query(m);
            if (matches.Any())
            {
                return true; // Near-duplicate found
            }
            
            // Insert under a new Guid key
            _lsh.Insert(Guid.NewGuid().ToString(), m);
            return false;
        }
    }
}
