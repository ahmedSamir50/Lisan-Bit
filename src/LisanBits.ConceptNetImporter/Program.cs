using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Neo4j.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("neo4j") ?? "bolt://localhost:7687";
var user = builder.Configuration["Neo4j:Username"] ?? "neo4j";
var pass = builder.Configuration["Neo4j:Password"] ?? "password";

builder.Services.AddSingleton<IDriver>(sp => GraphDatabase.Driver(connectionString, AuthTokens.Basic(user, pass)));
builder.Services.AddSingleton<ConceptNetImportService>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/api/conceptnet/status", (ConceptNetImportService importer) =>
{
    return Results.Ok(new
    {
        status = importer.Status.ToString(),
        progress = importer.Progress,
        downloadedBytes = importer.DownloadedBytes,
        totalBytes = importer.TotalBytes,
        processedLines = importer.ProcessedLines,
        importedEdges = importer.ImportedEdges,
        errorMessage = importer.ErrorMessage
    });
});

app.MapPost("/api/conceptnet/import", (ConceptNetImportService importer) =>
{
    var started = importer.StartImport();
    return started ? Results.Accepted("/api/conceptnet/status") : Results.Conflict("Import is already in progress.");
});

app.MapPost("/api/conceptnet/reset", async (ConceptNetImportService importer) =>
{
    try
    {
        await importer.ResetImportAsync();
        return Results.Ok("ConceptNet data reset successfully.");
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(ex.Message);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.Run();

public enum ImportStatus
{
    Idle,
    Downloading,
    Importing,
    Completed,
    Failed
}

public class ConceptNetImportService
{
    private readonly IDriver _driver;
    private readonly ILogger<ConceptNetImportService> _logger;
    private readonly string _dataDir = @"D:\A_S";
    private readonly string _conceptNetUrl = "https://s3.amazonaws.com/conceptnet/downloads/2019/edges/conceptnet-assertions-5.7.0.csv.gz";

    private readonly object _lock = new();
    private Task? _importTask;

    public ImportStatus Status { get; private set; } = ImportStatus.Idle;
    public double Progress { get; private set; }
    public long DownloadedBytes { get; private set; }
    public long TotalBytes { get; private set; }
    public long ProcessedLines { get; private set; }
    public long ImportedEdges { get; private set; }
    public string? ErrorMessage { get; private set; }

    public ConceptNetImportService(IDriver driver, ILogger<ConceptNetImportService> logger)
    {
        _driver = driver;
        _logger = logger;
    }

    public bool StartImport()
    {
        lock (_lock)
        {
            if (Status == ImportStatus.Downloading || Status == ImportStatus.Importing)
            {
                return false; // already running
            }

            Status = ImportStatus.Idle;
            Progress = 0;
            DownloadedBytes = 0;
            TotalBytes = 0;
            ProcessedLines = 0;
            ImportedEdges = 0;
            ErrorMessage = null;

            _importTask = Task.Run(RunImportAsync);
            return true;
        }
    }

    public async Task ResetImportAsync()
    {
        lock (_lock)
        {
            if (Status == ImportStatus.Downloading || Status == ImportStatus.Importing)
            {
                throw new InvalidOperationException("Cannot reset database while import is running.");
            }
        }

        Status = ImportStatus.Idle;
        Progress = 0;
        DownloadedBytes = 0;
        TotalBytes = 0;
        ProcessedLines = 0;
        ImportedEdges = 0;
        ErrorMessage = null;

        _logger.LogInformation("Resetting ConceptNet data in Neo4j...");
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var relTypes = new[] { "SYNONYM", "ANTONYM", "RELATED_TO", "IS_A", "PART_OF", "DERIVED_FROM", "ETYMOLOGICALLY_RELATED_TO" };
            foreach (var type in relTypes)
            {
                if (type == "DERIVED_FROM") continue; // keep Farasa derivations!
                await tx.RunAsync($"MATCH ()-[r:{type}]->() DETACH DELETE r");
            }
        });
        _logger.LogInformation("ConceptNet relationships successfully reset.");
    }

    private async Task RunImportAsync()
    {
        try
        {
            // 1. Ensure constraints are created in Neo4j
            _logger.LogInformation("Ensuring unique constraints exist in Neo4j...");
            await using (var session = _driver.AsyncSession())
            {
                await session.ExecuteWriteAsync(async tx =>
                {
                    await tx.RunAsync("CREATE CONSTRAINT root_text_unique IF NOT EXISTS FOR (r:Root) REQUIRE r.text IS UNIQUE");
                    await tx.RunAsync("CREATE CONSTRAINT word_text_unique IF NOT EXISTS FOR (w:Word) REQUIRE w.text IS UNIQUE");
                    await tx.RunAsync("CREATE CONSTRAINT context_name_unique IF NOT EXISTS FOR (c:Context) REQUIRE c.name IS UNIQUE");
                });
            }

            var filePath = Path.Combine(_dataDir, "conceptnet-assertions-5.7.0.csv.gz");
            Directory.CreateDirectory(_dataDir);

            // 2. Download file if not present
            if (!File.Exists(filePath))
            {
                Status = ImportStatus.Downloading;
                _logger.LogInformation("Downloading ConceptNet assertions from {Url} to {FilePath}...", _conceptNetUrl, filePath);
                
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromHours(2);
                using var response = await client.GetAsync(_conceptNetUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                TotalBytes = response.Content.Headers.ContentLength ?? 0;
                
                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    DownloadedBytes += bytesRead;
                    if (TotalBytes > 0)
                    {
                        Progress = Math.Round((double)DownloadedBytes / TotalBytes * 100, 2);
                    }
                }
                _logger.LogInformation("Download completed.");
            }
            else
            {
                _logger.LogInformation("ConceptNet assertions file already exists at {FilePath}. Skipping download.", filePath);
            }

            // 3. Start parsing and inserting
            Status = ImportStatus.Importing;
            Progress = 0;
            _logger.LogInformation("Parsing and importing assertions from {FilePath}...", filePath);

            await using var fileReadStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var gzipStream = new GZipStream(fileReadStream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzipStream, Encoding.UTF8);

            var batchSize = 10000;
            var batches = new Dictionary<string, List<ConceptEdge>>(StringComparer.OrdinalIgnoreCase);
            var currentBatchCount = 0;

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                ProcessedLines++;
                if (ProcessedLines % 500000 == 0)
                {
                    _logger.LogInformation("Processed {Count:N0} assertions lines. Imported {Edges:N0} edges.", ProcessedLines, ImportedEdges);
                }

                var parts = line.Split('\t');
                if (parts.Length < 5) continue;

                var rawRel = parts[1];
                var rawStart = parts[2];
                var rawEnd = parts[3];
                var metadataJson = parts[4];

                var startWord = ParseConcept(rawStart);
                var endWord = ParseConcept(rawEnd);

                if (startWord != null && endWord != null)
                {
                    var relType = NormalizeRelation(rawRel);
                    double weight = 1.0;
                    try
                    {
                        using var doc = JsonDocument.Parse(metadataJson);
                        if (doc.RootElement.TryGetProperty("weight", out var weightProp))
                        {
                            weight = weightProp.GetDouble();
                        }
                    }
                    catch { }

                    if (!batches.TryGetValue(relType, out var list))
                    {
                        list = new List<ConceptEdge>();
                        batches[relType] = list;
                    }

                    list.Add(new ConceptEdge { Start = startWord, End = endWord, Weight = weight });
                    currentBatchCount++;

                    if (currentBatchCount >= batchSize)
                    {
                        await FlushBatchesAsync(batches);
                        ImportedEdges += currentBatchCount;
                        currentBatchCount = 0;
                    }
                }
            }

            if (currentBatchCount > 0)
            {
                await FlushBatchesAsync(batches);
                ImportedEdges += currentBatchCount;
            }

            Status = ImportStatus.Completed;
            Progress = 100;
            _logger.LogInformation("Successfully imported {Edges:N0} ConceptNet assertions into Neo4j.", ImportedEdges);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during ConceptNet import.");
            Status = ImportStatus.Failed;
            ErrorMessage = ex.Message;
        }
    }

    private async Task FlushBatchesAsync(Dictionary<string, List<ConceptEdge>> batches)
    {
        foreach (var (relType, list) in batches)
        {
            if (list.Count == 0) continue;

            await using var session = _driver.AsyncSession();
            await session.ExecuteWriteAsync(async tx =>
            {
                var query = $@"
                    UNWIND $batch AS row
                    MERGE (s:Word {{text: row.Start}})
                    MERGE (e:Word {{text: row.End}})
                    MERGE (s)-[r:{relType}]->(e)
                    ON CREATE SET r.weight = row.Weight
                    ON MATCH SET r.weight = CASE WHEN row.Weight > r.weight THEN row.Weight ELSE r.weight END
                ";
                await tx.RunAsync(query, new { batch = list.Select(e => new { e.Start, e.End, e.Weight }).ToList() });
            });

            list.Clear();
        }
    }

    private static string? ParseConcept(string rawNode)
    {
        if (!rawNode.StartsWith("/c/ar/", StringComparison.OrdinalIgnoreCase))
            return null;
            
        var word = rawNode[6..]; // skip "/c/ar/"
        var slashIndex = word.IndexOf('/');
        if (slashIndex >= 0)
        {
            word = word[..slashIndex];
        }
        return word.Replace('_', ' ').Trim();
    }

    private static string NormalizeRelation(string rawRelation)
    {
        if (rawRelation.StartsWith("/r/"))
            rawRelation = rawRelation[3..];

        var sb = new StringBuilder();
        for (int i = 0; i < rawRelation.Length; i++)
        {
            char c = rawRelation[i];
            if (i > 0 && char.IsUpper(c))
            {
                sb.Append('_');
            }
            sb.Append(char.ToUpper(c));
        }
        return sb.ToString();
    }

    public class ConceptEdge
    {
        public string Start { get; set; } = string.Empty;
        public string End { get; set; } = string.Empty;
        public double Weight { get; set; } = 1.0;
    }
}
