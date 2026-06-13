using System.Diagnostics;
using LisanBits.DataPipeline.Data;
using LisanBits.DataPipeline.Data.Models;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;

namespace LisanBits.GraphSeeder;

public enum SeedingStatus
{
    Idle,
    Seeding,
    Completed,
    Failed
}

public class GraphSeedingService
{
    private readonly IDriver _driver;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GraphSeedingService> _logger;
    private readonly object _lock = new();
    private Task? _seedingTask;

    public SeedingStatus Status { get; private set; } = SeedingStatus.Idle;
    public double Progress { get; private set; }
    public int ProcessedCount { get; private set; }
    public int TotalEntries { get; private set; }
    public string? ErrorMessage { get; private set; }

    public GraphSeedingService(IDriver driver, IServiceProvider serviceProvider, ILogger<GraphSeedingService> logger)
    {
        _driver = driver;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public bool StartSeeding()
    {
        lock (_lock)
        {
            if (Status == SeedingStatus.Seeding)
                return false;

            Status = SeedingStatus.Idle;
            Progress = 0;
            ProcessedCount = 0;
            TotalEntries = 0;
            ErrorMessage = null;

            _seedingTask = Task.Run(() => RunSeedingAsync(CancellationToken.None));
            return true;
        }
    }

    public async Task ResetSeedingAsync()
    {
        lock (_lock)
        {
            if (Status == SeedingStatus.Seeding)
                throw new InvalidOperationException("Cannot reset database while seeding is in progress.");
        }

        Status = SeedingStatus.Idle;
        Progress = 0;
        ProcessedCount = 0;
        TotalEntries = 0;
        ErrorMessage = null;

        _logger.LogInformation("Resetting lexicon graph data in Neo4j...");
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            // Detach and delete all nodes corresponding to our schema
            await tx.RunAsync("MATCH (n) WHERE n:Word OR n:Root OR n:DialectWord DETACH DELETE n");
        });
        _logger.LogInformation("Lexicon graph data reset successfully.");
    }

    private async Task RunSeedingAsync(CancellationToken ct)
    {
        try
        {
            Status = SeedingStatus.Seeding;

            // 1. Ensure constraints in Neo4j
            _logger.LogInformation("Creating constraints in Neo4j if not exists...");
            await using (var session = _driver.AsyncSession())
            {
                await session.ExecuteWriteAsync(async tx =>
                {
                    await tx.RunAsync("CREATE CONSTRAINT root_text_unique IF NOT EXISTS FOR (r:Root) REQUIRE r.text IS UNIQUE");
                    await tx.RunAsync("CREATE CONSTRAINT word_text_unique IF NOT EXISTS FOR (w:Word) REQUIRE w.text IS UNIQUE");
                    await tx.RunAsync("CREATE CONSTRAINT dialect_word_text_unique IF NOT EXISTS FOR (d:DialectWord) REQUIRE d.text IS UNIQUE");
                });
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();

            TotalEntries = await db.LexiconEntries.CountAsync(ct);
            _logger.LogInformation("Found {Count} lexicon entries to seed.", TotalEntries);

            if (TotalEntries == 0)
            {
                _logger.LogWarning("No lexicon entries found in SQLite database. Make sure you run the scraping pipeline first.");
                Status = SeedingStatus.Completed;
                Progress = 100;
                return;
            }

            const int batchSize = 1000;
            while (ProcessedCount < TotalEntries && !ct.IsCancellationRequested)
            {
                var entries = await db.LexiconEntries
                    .OrderBy(e => e.Id)
                    .Skip(ProcessedCount)
                    .Take(batchSize)
                    .ToListAsync(ct);

                if (!entries.Any()) break;

                await SeedBatchAsync(entries);

                ProcessedCount += entries.Count;
                Progress = Math.Round((double)ProcessedCount / TotalEntries * 100, 2);
                _logger.LogInformation("Processed {Processed}/{Total} entries ({Progress}%).", ProcessedCount, TotalEntries, Progress);
            }

            Status = SeedingStatus.Completed;
            Progress = 100;
            _logger.LogInformation("Lexicon database seeding successfully finished.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during graph database seeding.");
            Status = SeedingStatus.Failed;
            ErrorMessage = ex.Message;
        }
    }

    private async Task SeedBatchAsync(List<LexiconEntry> entries)
    {
        var stdRoots = new List<object>();
        var stdSyns = new List<object>();
        var stdAnts = new List<object>();

        var dialectRoots = new List<object>();
        var dialectMaps = new List<object>();

        foreach (var entry in entries)
        {
            var word = entry.Word.Trim();
            var root = entry.Root.Trim();

            if (string.IsNullOrEmpty(word) || string.IsNullOrEmpty(root))
                continue;

            bool isDialect = entry.SourceBook.Contains("Taimoor", StringComparison.OrdinalIgnoreCase);

            if (isDialect)
            {
                dialectRoots.Add(new { Word = word, Root = root });

                if (!string.IsNullOrEmpty(entry.Synonyms))
                {
                    var synonyms = entry.Synonyms.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var syn in synonyms)
                    {
                        var synTrimmed = syn.Trim();
                        if (!string.IsNullOrEmpty(synTrimmed))
                        {
                            dialectMaps.Add(new { Word = word, Synonym = synTrimmed });
                        }
                    }
                }
            }
            else
            {
                stdRoots.Add(new { Word = word, Root = root });

                if (!string.IsNullOrEmpty(entry.Synonyms))
                {
                    var synonyms = entry.Synonyms.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var syn in synonyms)
                    {
                        var synTrimmed = syn.Trim();
                        if (!string.IsNullOrEmpty(synTrimmed))
                        {
                            stdSyns.Add(new { Word = word, Synonym = synTrimmed });
                        }
                    }
                }

                if (!string.IsNullOrEmpty(entry.Antonyms))
                {
                    var antonyms = entry.Antonyms.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var ant in antonyms)
                    {
                        var antTrimmed = ant.Trim();
                        if (!string.IsNullOrEmpty(antTrimmed))
                        {
                            stdAnts.Add(new { Word = word, Antonym = antTrimmed });
                        }
                    }
                }
            }
        }

        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            // 1. Standard Roots & Words
            if (stdRoots.Count > 0)
            {
                var query = @"
                    UNWIND $batch AS row
                    MERGE (r:Root {text: row.Root})
                    MERGE (w:Word {text: row.Word})
                    MERGE (w)-[:HAS_ROOT]->(r)
                ";
                await tx.RunAsync(query, new { batch = stdRoots });
            }

            // 2. Standard Synonyms
            if (stdSyns.Count > 0)
            {
                var query = @"
                    UNWIND $batch AS row
                    MERGE (w:Word {text: row.Word})
                    MERGE (s:Word {text: row.Synonym})
                    MERGE (w)-[:SYNONYM_OF]->(s)
                ";
                await tx.RunAsync(query, new { batch = stdSyns });
            }

            // 3. Standard Antonyms
            if (stdAnts.Count > 0)
            {
                var query = @"
                    UNWIND $batch AS row
                    MERGE (w:Word {text: row.Word})
                    MERGE (a:Word {text: row.Antonym})
                    MERGE (w)-[:ANTONYM_OF]->(a)
                ";
                await tx.RunAsync(query, new { batch = stdAnts });
            }

            // 4. Dialect Roots
            if (dialectRoots.Count > 0)
            {
                var query = @"
                    UNWIND $batch AS row
                    MERGE (r:Root {text: row.Root})
                    MERGE (dw:DialectWord {text: row.Word})
                    MERGE (dw)-[:SHARES_ROOT]->(r)
                ";
                await tx.RunAsync(query, new { batch = dialectRoots });
            }

            // 5. Dialect Mappings
            if (dialectMaps.Count > 0)
            {
                var query = @"
                    UNWIND $batch AS row
                    MERGE (dw:DialectWord {text: row.Word})
                    MERGE (w:Word {text: row.Synonym})
                    MERGE (dw)-[:DIALECT_MAPS_TO]->(w)
                ";
                await tx.RunAsync(query, new { batch = dialectMaps });
            }
        });
    }
}
