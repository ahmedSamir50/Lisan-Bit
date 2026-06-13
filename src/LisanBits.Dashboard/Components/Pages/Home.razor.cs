using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using LisanBits.DataPipeline.Data;
using LisanBits.DataPipeline.Data.Models;
using System.Net.Http.Json;
using LisanBits.Dashboard.Services;

namespace LisanBits.Dashboard.Components.Pages;

public partial class Home : ComponentBase, IDisposable
{
    [Inject]
    public IDbContextFactory<PipelineDbContext> DbFactory { get; set; } = default!;

    [Inject]
    public ScraperProgressService ProgressService { get; set; } = default!;

    [Inject]
    public IHttpClientFactory HttpClientFactory { get; set; } = default!;

    private bool IsStep1Running() => scrapeJobs != null && scrapeJobs.Any(j => j.Status == "InProgress");
    private bool IsStep1Completed() => lexiconCount > 0 && !IsStep1Running();

    private string GetStepStatusLabel(int step)
    {
        switch (step)
        {
            case 1:
                if (IsStep1Running()) return "Running";
                if (IsStep1Completed()) return "Completed";
                return "Pending Data";
            case 2:
                if (!IsStep1Completed()) return "Blocked";
                if (seederStatus?.Status == "Seeding") return "Running";
                if (seederStatus?.Status == "Completed") return "Completed";
                if (seederStatus?.Status == "Failed") return "Failed";
                return "Ready";
            case 3:
                if (seederStatus?.Status != "Completed") return "Blocked";
                if (importerStatus?.Status == "Downloading" || importerStatus?.Status == "Importing") return "Running";
                if (importerStatus?.Status == "Completed") return "Completed";
                if (importerStatus?.Status == "Failed") return "Failed";
                return "Ready";
            case 4:
                if (seederStatus?.Status != "Completed") return "Blocked";
                if (grammarStatus?.Status == "Ingesting" || grammarStatus?.Status == "Mining" || grammarStatus?.Status == "Training") return "Running";
                if (grammarStatus?.Status == "Completed") return "Completed";
                if (grammarStatus?.Status == "Failed") return "Failed";
                return "Ready";
            default:
                return "Unknown";
        }
    }

    private string GetStepBorderClass(int step)
    {
        var status = GetStepStatusLabel(step);
        switch (status)
        {
            case "Blocked": return "border-secondary opacity-50";
            case "Pending Data": return "border-warning border-dashed";
            case "Ready": return "border-info border-glow-info";
            case "Running": return "border-primary border-glow-active";
            case "Completed": return "border-success";
            case "Failed": return "border-danger";
            default: return "border-secondary";
        }
    }

    private string GetStepTextClass(int step)
    {
        var status = GetStepStatusLabel(step);
        switch (status)
        {
            case "Blocked": return "text-muted";
            case "Pending Data": return "text-warning";
            case "Ready": return "text-info";
            case "Running": return "text-primary fw-bold";
            case "Completed": return "text-success fw-bold";
            case "Failed": return "text-danger fw-bold";
            default: return "text-white";
        }
    }

    private string GetStepBadgeClass(int step)
    {
        var status = GetStepStatusLabel(step);
        switch (status)
        {
            case "Blocked": return "bg-secondary text-muted opacity-50";
            case "Pending Data": return "bg-warning text-dark";
            case "Ready": return "bg-info text-dark";
            case "Running": return "bg-primary text-white";
            case "Completed": return "bg-success text-white";
            case "Failed": return "bg-danger text-white";
            default: return "bg-secondary";
        }
    }

    private string GetConnectorGradient(int fromStep, int toStep)
    {
        var fromStatus = GetStepStatusLabel(fromStep);
        var toStatus = GetStepStatusLabel(toStep);
        
        string fromColor = "rgba(148, 163, 184, 0.2)"; // text-secondary with opacity
        if (fromStatus == "Completed") fromColor = "#10b981"; // success green
        else if (fromStatus == "Running") fromColor = "#3b82f6"; // primary blue

        string toColor = "rgba(148, 163, 184, 0.2)";
        if (toStatus == "Completed") toColor = "#10b981";
        else if (toStatus == "Running" || toStatus == "Ready") toColor = "#3b82f6";

        return $"linear-gradient(to right, {fromColor}, {toColor})";
    }

    private ScrapeJob[]? scrapeJobs;
    private CategoryStat[]? categoryStats;
    private SourceStat[]? sourceStats;
    private DataSourceConfig[]? configs;
    private DataSourceConfig newConfig = new DataSourceConfig();
    private ConceptNetStatus? importerStatus;
    private GraphSeederStatus? seederStatus;
    private GrammarStatus? grammarStatus;
    private int lexiconCount = 0;
    private System.Threading.Timer? _refreshTimer;
    private DateTime _lastLoadTime = DateTime.MinValue;
    private readonly System.Threading.SemaphoreSlim _loadLock = new System.Threading.SemaphoreSlim(1, 1);
    private int? totalDuplicates = null;
    private bool isScanningDuplicates = false;
    private bool isRemovingDuplicates = false;

    protected override async Task OnInitializedAsync()
    {
        await LoadData(force: true);

        // Polling remains as a fallback when the worker's hub connection drops.
        _refreshTimer = new System.Threading.Timer(async _ =>
        {
            await InvokeAsync(async () =>
            {
                await LoadData(force: true);
                StateHasChanged();
            });
        }, null, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(25));
        
        ProgressService.OnProgressUpdated += HandleProgressUpdated;
    }

    private void HandleProgressUpdated(string source, int index)
    {
        if (scrapeJobs != null)
        {
            var job = scrapeJobs.FirstOrDefault(j => j.SourceName == source);
            if (job != null)
            {
                job.LastProcessedIndex = index;
                job.Status = "InProgress";
                job.LastUpdatedAt = DateTime.UtcNow;
            }
        }
        
        InvokeAsync(async () =>
        {
            await LoadData(force: true);
            StateHasChanged();
        });
    }

    private async Task LoadData(bool force = false)
    {
        if (!force && DateTime.UtcNow - _lastLoadTime < TimeSpan.FromSeconds(10))
        {
            return;
        }

        await _loadLock.WaitAsync();
        try
        {
            if (!force && DateTime.UtcNow - _lastLoadTime < TimeSpan.FromSeconds(10))
            {
                return;
            }

            using var db = DbFactory.CreateDbContext();
            
            scrapeJobs = await db.ScrapeJobs.ToArrayAsync();
            configs = await db.DataSourceConfigs.ToArrayAsync();
            lexiconCount = await db.LexiconEntries.CountAsync();
            
            categoryStats = await db.RawUniversalData
                .GroupBy(d => d.Category)
                .Select(g => new CategoryStat { 
                    Category = g.Key, 
                    TotalSentences = g.Sum(d => d.SentenceCount),
                    TotalWords = g.Sum(d => d.WordCount)
                }).ToArrayAsync();
                
            sourceStats = await db.RawUniversalData
                .GroupBy(d => d.Source)
                .Select(g => new SourceStat { 
                    Source = g.Key, 
                    TotalSentences = g.Sum(d => d.SentenceCount),
                    TotalRows = g.Count(),
                    TotalWords = g.Sum(d => (long)d.WordCount)
                }).ToArrayAsync();

            _lastLoadTime = DateTime.UtcNow;

            // Fetch ConceptNet importer service status
            try
            {
                using var client = HttpClientFactory.CreateClient("ConceptNetImporter");
                importerStatus = await client.GetFromJsonAsync<ConceptNetStatus>("api/conceptnet/status");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching ConceptNet status: {ex.Message}");
            }

            // Fetch Graph Seeder service status
            try
            {
                using var client = HttpClientFactory.CreateClient("GraphSeeder");
                seederStatus = await client.GetFromJsonAsync<GraphSeederStatus>("api/seeder/status");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching Graph Seeder status: {ex.Message}");
            }

            // Fetch Grammar pipeline service status
            try
            {
                using var client = HttpClientFactory.CreateClient("GrammarPipeline");
                grammarStatus = await client.GetFromJsonAsync<GrammarStatus>("api/grammar/status");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching Grammar status: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching data: {ex.Message}");
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task AddSource()
    {
        if(string.IsNullOrWhiteSpace(newConfig.Name) || string.IsNullOrWhiteSpace(newConfig.BaseUrl)) return;
        
        using var db = DbFactory.CreateDbContext();
        newConfig.IsActive = true;
        db.DataSourceConfigs.Add(newConfig);
        await db.SaveChangesAsync();
        
        newConfig = new DataSourceConfig(); // reset form
        await LoadData(force: true);
    }

    private async Task ResetDatabase()
    {
        using var db = DbFactory.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM RawUniversalData");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ScrapeJobs");
        await LoadData(force: true);
    }

    private async Task ResetSource(string sourceName)
    {
        using var db = DbFactory.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM RawUniversalData WHERE Source = {0}", sourceName);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ScrapeJobs WHERE SourceName = {0}", sourceName);
        await LoadData(force: true);
    }

    private async Task TriggerImport()
    {
        try
        {
            using var client = HttpClientFactory.CreateClient("ConceptNetImporter");
            var response = await client.PostAsync("api/conceptnet/import", null);
            if (response.IsSuccessStatusCode)
            {
                await LoadData(force: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error triggering import: {ex.Message}");
        }
    }

    private async Task ResetConceptNet()
    {
        try
        {
            using var client = HttpClientFactory.CreateClient("ConceptNetImporter");
            var response = await client.PostAsync("api/conceptnet/reset", null);
            if (response.IsSuccessStatusCode)
            {
                await LoadData(force: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error resetting ConceptNet data: {ex.Message}");
        }
    }

    private async Task TriggerSeeding()
    {
        try
        {
            using var client = HttpClientFactory.CreateClient("GraphSeeder");
            var response = await client.PostAsync("api/seeder/import", null);
            if (response.IsSuccessStatusCode)
            {
                await LoadData(force: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error triggering seeding: {ex.Message}");
        }
    }

    private async Task ResetGraph()
    {
        try
        {
            using var client = HttpClientFactory.CreateClient("GraphSeeder");
            var response = await client.PostAsync("api/seeder/reset", null);
            if (response.IsSuccessStatusCode)
            {
                await LoadData(force: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error resetting lexicon graph: {ex.Message}");
        }
    }

    private async Task TriggerGrammarIngest()
    {
        try
        {
            using var client = HttpClientFactory.CreateClient("GrammarPipeline");
            var response = await client.PostAsync("api/grammar/ingest", null);
            if (response.IsSuccessStatusCode)
            {
                await LoadData(force: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error triggering grammar ingest: {ex.Message}");
        }
    }

    private async Task TriggerGrammarMine()
    {
        try
        {
            using var client = HttpClientFactory.CreateClient("GrammarPipeline");
            var response = await client.PostAsync("api/grammar/mine", null);
            if (response.IsSuccessStatusCode)
            {
                await LoadData(force: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error triggering grammar mine: {ex.Message}");
        }
    }

    private async Task TriggerGrammarTrain()
    {
        try
        {
            using var client = HttpClientFactory.CreateClient("GrammarPipeline");
            var response = await client.PostAsync("api/grammar/train", null);
            if (response.IsSuccessStatusCode)
            {
                await LoadData(force: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error triggering grammar train: {ex.Message}");
        }
    }

    private async Task ResetGrammar()
    {
        try
        {
            using var client = HttpClientFactory.CreateClient("GrammarPipeline");
            var response = await client.PostAsync("api/grammar/reset", null);
            if (response.IsSuccessStatusCode)
            {
                await LoadData(force: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error resetting grammar data: {ex.Message}");
        }
    }

    private string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:0.0} {suffixes[counter]}";
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
        ProgressService.OnProgressUpdated -= HandleProgressUpdated;
    }

    private async Task ScanForDuplicates()
    {
        isScanningDuplicates = true;
        totalDuplicates = null;
        StateHasChanged();
        
        try
        {
            using var db = DbFactory.CreateDbContext();
            await db.Database.OpenConnectionAsync();
            using var command = db.Database.GetDbConnection().CreateCommand();
            
            command.CommandText = @"
                SELECT SUM(DuplicateRows) AS TotalDuplicates
                FROM (
                    SELECT COUNT(*) - 1 AS DuplicateRows
                    FROM RawUniversalData
                    GROUP BY Source, TextContent
                    HAVING COUNT(*) > 1
                )";
                
            var result = await command.ExecuteScalarAsync();
            totalDuplicates = (result != DBNull.Value && result != null) ? Convert.ToInt32(result) : 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error scanning for duplicates: {ex.Message}");
            totalDuplicates = 0;
        }
        finally
        {
            isScanningDuplicates = false;
            StateHasChanged();
        }
    }

    private async Task RemoveDuplicates()
    {
        isRemovingDuplicates = true;
        StateHasChanged();
        
        try
        {
            using var db = DbFactory.CreateDbContext();
            
            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM RawUniversalData 
                WHERE rowid NOT IN (
                    SELECT MIN(rowid) 
                    FROM RawUniversalData 
                    GROUP BY Source, TextContent
                )");

            totalDuplicates = 0;
            await LoadData(force: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error removing duplicates: {ex.Message}");
        }
        finally
        {
            isRemovingDuplicates = false;
            StateHasChanged();
        }
    }

    public class ConceptNetStatus
    {
        public string Status { get; set; } = "Idle";
        public double Progress { get; set; }
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public long ProcessedLines { get; set; }
        public long ImportedEdges { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class GraphSeederStatus
    {
        public string Status { get; set; } = "Idle";
        public double Progress { get; set; }
        public int ProcessedCount { get; set; }
        public int TotalEntries { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class GrammarStatus
    {
        public string Status { get; set; } = "Idle";
        public double Progress { get; set; }
        public long ProcessedItems { get; set; }
        public long TotalItems { get; set; }
        public long MergedNodes { get; set; }
        public long MergedEdges { get; set; }
        public double ModelAccuracy { get; set; }
        public string? ErrorMessage { get; set; }
        public string[]? Logs { get; set; }
    }

    public class CategoryStat
    {
        public string Category { get; set; } = string.Empty;
        public int TotalSentences { get; set; }
        public int TotalWords { get; set; }
    }

    public class SourceStat
    {
        public string Source { get; set; } = string.Empty;
        public int TotalSentences { get; set; }
        public int TotalRows { get; set; }
        public long TotalWords { get; set; }
    }
}
