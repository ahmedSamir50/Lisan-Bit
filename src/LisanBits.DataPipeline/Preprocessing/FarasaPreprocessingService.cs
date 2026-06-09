using System.Text;
using System.Text.Json;
using LisanBits.DataPipeline.Data;
using LisanBits.DataPipeline.Data.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Extentions.StringExt;

namespace LisanBits.DataPipeline.Preprocessing;

public class FarasaToken
{
    public string Word { get; set; } = string.Empty;
    public string Root { get; set; } = string.Empty;
    public string Pos { get; set; } = string.Empty;
}

public class FarasaPreprocessingService : BackgroundService
{
    private readonly ILogger<FarasaPreprocessingService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly HttpClient _httpClient;
    private readonly Uri _analyzeUri;
    private readonly bool _allowFallback;
    private readonly DateTime _reprocessCutoffUtc;
    private readonly Neo4jService _neo4jService;
    private bool _initialReprocessComplete;
    private Dictionary<string, string> _lexiconCache = new(StringComparer.OrdinalIgnoreCase);
    private int _lastLoadedLexiconCount = -1;
    
    // We process sentences in batches
    private const int BATCH_SIZE = 50;

    public FarasaPreprocessingService(
        ILogger<FarasaPreprocessingService> logger, 
        IServiceProvider serviceProvider, 
        HttpClient httpClient,
        IConfiguration configuration,
        Neo4jService neo4jService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _httpClient = httpClient;
        _neo4jService = neo4jService;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        
        // BaseAddress is set by Program.cs from Aspire-injected environment variables
        // It should be the actual resolved address (not just the service name)
        var baseUri = _httpClient.BaseAddress ?? new Uri("http://localhost:8080/", UriKind.Absolute);
        var analyzePath = configuration["FarasaApi:AnalyzePath"] ?? "analyze";
        if (analyzePath.StartsWith('/'))
            analyzePath = analyzePath[1..];
        _analyzeUri = new Uri(baseUri, analyzePath);
        _allowFallback = bool.TryParse(configuration["FarasaApi:AllowFallback"], out var parsed) && parsed;
        _reprocessCutoffUtc = DateTime.UtcNow;
        _initialReprocessComplete = false;

        _logger.LogInformation("Farasa endpoint configured: {FarasaAnalyzeUri}", _analyzeUri);
        _logger.LogInformation("Farasa fallback mode: {Mode}", _allowFallback ? "ENABLED" : "DISABLED (strict)");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Farasa Preprocessing Service started.");

        try
        {
            await _neo4jService.EnsureDatabaseConnectedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Neo4j database on startup. Service will proceed, but graph database updates may fail.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();

                // Load or refresh Golden Standard Lexicon cache dynamically
                await EnsureLexiconCacheLoadedAsync(db, stoppingToken);

                // First pass after startup: repair existing processed rows in-place (no deletion).
                if (!_initialReprocessComplete)
                {
                    var existingBatch = await (
                        from p in db.ProcessedUniversalData
                        join r in db.RawUniversalData on p.RawDataId equals r.Id
                        where p.ProcessedAt < _reprocessCutoffUtc
                        orderby p.Id
                        select new { Processed = p, Raw = r })
                        .Take(BATCH_SIZE)
                        .ToListAsync(stoppingToken);

                    if (existingBatch.Any())
                    {
                        _logger.LogInformation("Repairing {Count} existing processed rows in-place.", existingBatch.Count);

                        foreach (var pair in existingBatch)
                        {
                            if (stoppingToken.IsCancellationRequested) break;
                            try
                            {
                                var analysis = await AnalyzeAsync(db, pair.Raw.TextContent, pair.Raw.Category, pair.Raw.Id, stoppingToken);
                                pair.Processed.ProcessedText = pair.Raw.TextContent;
                                pair.Processed.RootSequence = analysis.RootSequence;
                                pair.Processed.PosSequence = analysis.PosSequence;
                                pair.Processed.ContextVector = analysis.ContextVectorJson;
                                pair.Processed.ProcessedAt = DateTime.UtcNow;

                                // Insert roots and words into Neo4j
                                var contextVector = JsonSerializer.Deserialize<Dictionary<string, double>>(analysis.ContextVectorJson);
                                if (contextVector != null)
                                {
                                    var engine = new SyntacticEngine();
                                    foreach (var token in analysis.Tokens)
                                    {
                                        var grammaticalState = engine.ProcessToken(token.Word, token.Pos, token.Root, token.Word);
                                        foreach (var (ctx, weight) in contextVector)
                                        {
                                            await _neo4jService.InsertRootAndWordAsync(token.Root, token.Word, token.Pos, weight, ctx);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error repairing ProcessedId {ProcessedId} / RawDataId {RawId}", pair.Processed.Id, pair.Raw.Id);
                            }
                        }

                        await db.SaveChangesAsync(stoppingToken);
                        continue;
                    }

                    _initialReprocessComplete = true;
                    _logger.LogInformation("Initial in-place reprocessing completed. Switching to normal unprocessed flow.");
                }

                // Find raw data that has not been processed yet
                // We do a simple LEFT JOIN equivalent or just check if it exists in Processed
                var unprocessedBatch = await db.RawUniversalData
                    .Where(r => !db.ProcessedUniversalData.Any(p => p.RawDataId == r.Id))
                    .OrderBy(r => r.Id)
                    .Take(BATCH_SIZE)
                    .ToListAsync(stoppingToken);

                if (!unprocessedBatch.Any())
                {
                    // If caught up, wait before checking again
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                _logger.LogInformation("Preprocessing {Count} raw items with Farasa.", unprocessedBatch.Count);

                foreach (var item in unprocessedBatch)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        var analysis = await AnalyzeAsync(db, item.TextContent, item.Category, item.Id, stoppingToken);

                        var processedData = new ProcessedUniversalData
                        {
                            RawDataId = item.Id,
                            ProcessedText = item.TextContent,
                            RootSequence = analysis.RootSequence,
                            PosSequence  = analysis.PosSequence,
                            ContextVector = analysis.ContextVectorJson,
                            ProcessedAt = DateTime.UtcNow
                        };

                        db.ProcessedUniversalData.Add(processedData);

                        // Insert roots and words into Neo4j
                        var contextVector = JsonSerializer.Deserialize<Dictionary<string, double>>(analysis.ContextVectorJson);
                        if (contextVector != null)
                        {
                            var engine = new SyntacticEngine();
                            foreach (var token in analysis.Tokens)
                            {
                                var grammaticalState = engine.ProcessToken(token.Word, token.Pos, token.Root, token.Word);
                                foreach (var (ctx, weight) in contextVector)
                                {
                                    await _neo4jService.InsertRootAndWordAsync(token.Root, token.Word, token.Pos, weight, ctx);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing RawDataId {Id}", item.Id);
                    }
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Farasa Preprocessing loop.");
                await Task.Delay(5000, stoppingToken); // Backoff on error
            }
        }
    }

    private async Task<(string RootSequence, string PosSequence, string ContextVectorJson, List<FarasaToken> Tokens)> AnalyzeAsync(
        PipelineDbContext db,
        string text,
        string category,
        int rawDataId,
        CancellationToken stoppingToken)
    {
        var requestBody = new { text };
        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        var tokens = new List<FarasaToken>();

        try
        {
            var response = await _httpClient.PostAsync(_analyzeUri, content, stoppingToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(stoppingToken);
                using var doc = JsonDocument.Parse(json);
                foreach (var tokenElement in doc.RootElement.GetProperty("tokens").EnumerateArray())
                {
                    tokens.Add(new FarasaToken
                    {
                        Word = tokenElement.GetProperty("word").GetString() ?? string.Empty,
                        Root = tokenElement.GetProperty("root").GetString() ?? string.Empty,
                        Pos = tokenElement.GetProperty("pos").GetString() ?? string.Empty
                    });
                }
            }
            else
            {
                if (_allowFallback)
                {
                    _logger.LogWarning("Farasa API returned {Status} for RawDataId {Id}. Using fallback.", response.StatusCode, rawDataId);
                    tokens = FallbackTokenize(text);
                }
                else
                {
                    throw new InvalidOperationException($"Farasa API returned status {(int)response.StatusCode} for RawDataId {rawDataId} and fallback is disabled.");
                }
            }
        }
        catch (Exception httpEx)
        {
            if (_allowFallback)
            {
                _logger.LogWarning(httpEx, "Farasa HTTP call failed for RawDataId {Id}. Using fallback.", rawDataId);
                tokens = FallbackTokenize(text);
            }
            else
            {
                throw;
            }
        }

        // Apply Lookup-First Strategy: Override statistical roots using Golden Standard Lexicon
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token.Word)) continue;

            string normWord = token.Word.RemoveTashkeel().Trim();
            
            // Strip common prefixes to ensure base lemmas match dictionary keys
            if (normWord.StartsWith("ال")) normWord = normWord.Substring(2);
            else if (normWord.StartsWith("وال")) normWord = normWord.Substring(3);
            else if (normWord.StartsWith("بال")) normWord = normWord.Substring(3);
            else if (normWord.StartsWith("كال")) normWord = normWord.Substring(3);
            else if (normWord.StartsWith("لل")) normWord = normWord.Substring(2);
            else
            {
                if (normWord.StartsWith("و") && normWord.Length > 3) normWord = normWord.Substring(1);
                else if (normWord.StartsWith("ف") && normWord.Length > 3) normWord = normWord.Substring(1);
            }

            if (_lexiconCache.TryGetValue(normWord, out var goldenRoot))
            {
                if (!string.IsNullOrEmpty(goldenRoot))
                {
                    token.Root = goldenRoot;
                }
            }
        }

        var rootSequence = string.Join(" ", tokens.Select(t => t.Root));
        var posSequence = string.Join(" ", tokens.Select(t => t.Pos));

        var contextVector = BuildContextVector(category, posSequence);
        var contextVectorJson = JsonSerializer.Serialize(contextVector);
        return (rootSequence, posSequence, contextVectorJson, tokens);
    }

    private async Task EnsureLexiconCacheLoadedAsync(PipelineDbContext db, CancellationToken ct)
    {
        try
        {
            var dbCount = await db.LexiconEntries.CountAsync(ct);
            if (dbCount == _lastLoadedLexiconCount) return;

            _logger.LogInformation("Loading Golden Standard Lexicon cache from database (Count: {DbCount})...", dbCount);
            
            var entries = await db.LexiconEntries
                .Select(e => new { e.Word, e.Root })
                .ToListAsync(ct);

            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                string normWord = RemoveTashkeel(entry.Word).Trim();
                if (!string.IsNullOrEmpty(normWord) && !cache.ContainsKey(normWord))
                {
                    cache[normWord] = entry.Root;
                }
            }

            _lexiconCache = cache;
            _lastLoadedLexiconCount = dbCount;
            _logger.LogInformation("Loaded {Count} words into Golden Standard Lexicon cache.", _lexiconCache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Golden Standard Lexicon cache.");
        }
    }

    

    // Category -> (secondary domain, weight) pairs reflecting realistic domain overlap.
    private static readonly Dictionary<string, (string Domain, double Weight)[]> CategoryOverlap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Religion"]   = new[] { ("History",    0.12), ("Linguistics", 0.08) },
            ["Medical"]    = new[] { ("Science",    0.15), ("Research",    0.05) },
            ["Science"]    = new[] { ("Technology", 0.10), ("Research",    0.10) },
            ["Finance"]    = new[] { ("News",        0.12), ("Science",     0.08) },
            ["Sports"]     = new[] { ("News",        0.15), ("DailyLife",   0.05) },
            ["News"]       = new[] { ("Politics",   0.10), ("DailyLife",   0.10) },
            ["Literature"] = new[] { ("History",    0.12), ("Linguistics", 0.08) },
            ["DailyLife"]  = new[] { ("Slang",       0.10), ("General",     0.10) },
            ["Slang"]      = new[] { ("DailyLife",   0.12), ("General",     0.08) },
            ["Linguistics"]= new[] { ("Literature", 0.12), ("History", 0.08) },
        };

    private static Dictionary<string, double> BuildContextVector(string primaryCategory, string posSequence)
    {
        var vector = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double primary = 0.80;

        // Increase primary weight slightly when POS sequence is heavily nominal (content-rich)
        if (!string.IsNullOrEmpty(posSequence))
        {
            var tags = posSequence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            double nounRatio = tags.Count(t => t.StartsWith("N", StringComparison.OrdinalIgnoreCase)) / (double)Math.Max(1, tags.Length);
            primary = Math.Min(0.90, 0.75 + nounRatio * 0.15);
        }

        vector[primaryCategory] = Math.Round(primary, 2);

        if (CategoryOverlap.TryGetValue(primaryCategory, out var overlaps))
        {
            double remaining = 1.0 - primary;
            double totalOverlapWeight = overlaps.Sum(o => o.Weight);
            foreach (var (domain, weight) in overlaps)
            {
                vector[domain] = Math.Round(remaining * (weight / totalOverlapWeight), 2);
            }
        }

        return vector;
    }

    private static List<FarasaToken> FallbackTokenize(string text)
    {
        var tokens = new List<FarasaToken>();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = word.TrimStart('\u0627', '\u0644'); // strip \u0627\u0644
            var root = clean.Length >= 3 ? clean[..3] : clean;
            var pos = word.StartsWith('\u0627') ? "NOUN" : "VERB";
            tokens.Add(new FarasaToken { Word = word, Root = root, Pos = pos });
        }
        return tokens;
    }
}
