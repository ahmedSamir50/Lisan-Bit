using LisanBits.DataPipeline.Acquisition;
using LisanBits.DataPipeline.Data;
using System.IO.Compression;
using LisanBits.DataPipeline.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.AspNetCore.SignalR.Client;

namespace LisanBits.DataPipeline;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly UniversalHtmlScraper _scraper;
    private readonly LisanBits.DataPipeline.Preprocessing.DataCleaner _dataCleaner;
    private readonly SemaphoreSlim _dbWriteLock = new SemaphoreSlim(1, 1);
    private readonly SemaphoreSlim _datasetPrepLock = new SemaphoreSlim(1, 1);
    private readonly string _dashboardHubUrl;
    private readonly IConfiguration _configuration;
    private HubConnection? _hubConnection;

    public Worker(ILogger<Worker> logger, IServiceProvider serviceProvider, UniversalHtmlScraper scraper, IConfiguration configuration, LisanBits.DataPipeline.Preprocessing.DataCleaner dataCleaner)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _scraper = scraper;
        _configuration = configuration;
        _dataCleaner = dataCleaner;
        
        // Aspire injects project endpoints as services__lisanbits-dashboard__http__0
        // which maps to services:lisanbits-dashboard:http:0 in IConfiguration.
        var dashboardEndpoint = configuration["services:lisanbits-dashboard:http:0"]
                              ?? configuration["services:lisanbits-dashboard:https:0"]
                              ?? configuration["DashboardHubUrl"]
                              ?? "http://localhost:5241";
        
        // Ensure the endpoint doesn't already have the hub path
        if (dashboardEndpoint.EndsWith("/scraperhub"))
        {
            _dashboardHubUrl = dashboardEndpoint;
        }
        else
        {
            if (dashboardEndpoint.EndsWith("/"))
                dashboardEndpoint = dashboardEndpoint[..^1];
            _dashboardHubUrl = $"{dashboardEndpoint}/scraperhub";
        }
        
        _logger.LogInformation("Dashboard SignalR hub URL resolved to: {DashboardHubUrl}", _dashboardHubUrl);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Data Pipeline Worker starting.");
        
        // Connect to Dashboard SignalR Hub
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(_dashboardHubUrl)
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.Reconnecting += error =>
        {
            _logger.LogWarning(error, "Reconnecting to Dashboard SignalR Hub at {DashboardHubUrl}.", _dashboardHubUrl);
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += connectionId =>
        {
            _logger.LogInformation("Reconnected to Dashboard SignalR Hub at {DashboardHubUrl}. ConnectionId: {ConnectionId}", _dashboardHubUrl, connectionId);
            return Task.CompletedTask;
        };

        _hubConnection.Closed += error =>
        {
            _logger.LogWarning(error, "Dashboard SignalR Hub connection closed at {DashboardHubUrl}.", _dashboardHubUrl);
            return Task.CompletedTask;
        };
            
        try 
        {
            await _hubConnection.StartAsync(stoppingToken);
            _logger.LogInformation("Connected to Dashboard SignalR Hub at {DashboardHubUrl}.", _dashboardHubUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not connect to Dashboard SignalR Hub at {DashboardHubUrl}.", _dashboardHubUrl);
        }

        // Sync config updates and reset stuck Processing URLs to Pending on startup
        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();
                
                // 1. Sync config updates (XPaths & IsActive) in database
                var configs = await db.DataSourceConfigs.ToListAsync(stoppingToken);
                bool dbChanged = false;

                // Read slang dataset path from config so source 29 BaseUrl is never hard-coded in seed data
                var slangCsvPath = (_configuration["SlangDataset:CsvPath"] ?? @"D:\A_S\nofal_slang.csv").Replace('\\', '/');
                var cmpCorrectedPath = (_configuration["SlangDataset:CmpCorrectedPath"] ?? @"D:\A_S\arb-egy-cmp-corpus_corrected.json").Replace('\\', '/');

                void UpdateConfig(int id, Action<DataSourceConfig> updateAction)
                {
                    var config = configs.FirstOrDefault(c => c.Id == id);
                    if (config != null)
                    {
                        var beforeBaseUrl = config.BaseUrl;
                        var beforeCategory = config.Category;
                        var beforeDiscovery = config.DiscoveryXPath;
                        var beforeLink = config.LinkXPath;
                        var beforeTarget = config.TargetXPath;
                        var beforeActive = config.IsActive;
                        
                        updateAction(config);
                        
                        if (config.BaseUrl != beforeBaseUrl ||
                            config.Category != beforeCategory ||
                            config.DiscoveryXPath != beforeDiscovery || 
                            config.LinkXPath != beforeLink || 
                            config.TargetXPath != beforeTarget ||
                            config.IsActive != beforeActive)
                        {
                            dbChanged = true;
                        }
                    }
                }
                
                // Hsoub Academy (All) - Id 10
                UpdateConfig(10, c => { c.IsActive = true; });
                
                // Investing.com - Id 17
                UpdateConfig(17, c => 
                {
                    c.IsActive = true;
                    c.LinkXPath = "//div[contains(@class, 'textDiv')]//a | //div[contains(@class, 'midDiv')]//a | //div[contains(@class, 'sideDiv')]//a";
                    c.DiscoveryXPath = "//div[contains(@class, 'textDiv')]//a | //div[contains(@class, 'midDiv')]//a | //div[contains(@class, 'sideDiv')]//a";
                });
                
                // Al-Eqtisad - Id 18
                UpdateConfig(18, c => 
                {
                    c.IsActive = true;
                    c.TargetXPath = "//div[contains(@class, 'article-content')]/div[not(@class) or @class='']";
                    c.LinkXPath = "//h3//a | //h2//a | //div[contains(@class, 'flex-col')]//a";
                    c.DiscoveryXPath = "//h3//a | //h2//a | //div[contains(@class, 'flex-col')]//a";
                });
                
                // Kooora - Id 21
                UpdateConfig(21, c => 
                {
                    c.IsActive = true;
                    c.Category = "Sports";
                    c.LinkXPath = "//td[contains(@class, 'news_title')]//a | //a[contains(@href, '?n=')] | //a[contains(@href, '%D8%A3%D8%AE%D8%A8%D8%A7%D8%B1') or contains(@href, '/أخبار/')]";
                    c.DiscoveryXPath = "//td[contains(@class, 'news_title')]//a | //a[contains(@href, '?n=')] | //a[contains(@href, '%D8%A3%D8%AE%D8%A8%D8%A7%D8%B1') or contains(@href, '/أخبار/')]";
                });

                // Egyptian Slang (Nofal) - Id 20 (offline import, not a live crawler)
                UpdateConfig(20, c => { c.IsActive = false; });
                
                // Al Jazeera - Id 22 (Deactivate due to TLS Handshake CDN timeouts/blocks)
                UpdateConfig(22, c => { c.IsActive = false; });
                
                // Sky News Arabia - Id 23 (Disable until XPath/source is repaired)
                UpdateConfig(23, c => 
                {
                    c.IsActive = false;
                    c.LinkXPath = "//a[contains(@class, 'article-title') or contains(@href, '/world/') or contains(@href, '/middle-east/') or contains(@href, '/live-story/')]";
                    c.DiscoveryXPath = "//a[contains(@class, 'article-title') or contains(@href, '/world/') or contains(@href, '/middle-east/') or contains(@href, '/live-story/')]";
                });
                
                // Hindawi (Novels) - Id 19 (Skip/Deactivate)
                UpdateConfig(19, c => { c.IsActive = false; });
                
                // WikiHow Arabic - Id 24 (Skip/Deactivate)
                UpdateConfig(24, c => { c.IsActive = false; });

                // Youm7 - Id 25
                UpdateConfig(25, c => { c.IsActive = true; });

                // Masrawy - Id 26
                UpdateConfig(26, c => { c.IsActive = true; });

                // Wikipedia (Literature) - Id 27
                UpdateConfig(27, c => { c.IsActive = true; });

                // Wikipedia (DailyLife) - Id 28
                UpdateConfig(28, c => { c.IsActive = true; });

                // Nofal Slang (Local CSV) - Id 29
                // BaseUrl is driven by SlangDataset:CsvPath config, never hard-coded.
                UpdateConfig(29, c => 
                { 
                    c.BaseUrl = $"file:///{slangCsvPath}"; 
                    c.IsActive = true; 
                });

                // ARB-EGY-CMP (Local JSON) - Id 30
                // BaseUrl is driven by SlangDataset:CmpCorrectedPath config, never hard-coded.
                UpdateConfig(30, c => { c.BaseUrl = $"file:///{cmpCorrectedPath}"; });


                
                if (dbChanged)
                {
                    _logger.LogInformation("Updating data source configurations in database.");
                    await _dbWriteLock.WaitAsync(stoppingToken);
                    try
                    {
                        await db.SaveChangesAsync(stoppingToken);
                    }
                    finally
                    {
                        _dbWriteLock.Release();
                    }
                }

                
                // 2. Reset stuck 'Processing' URLs
                var stuckUrls = await db.CrawledUrlQueue.Where(q => q.Status == "Processing")
                                                .ToListAsync(stoppingToken);
                    
                if (stuckUrls.Any())
                {
                    _logger.LogInformation("Found {Count} stuck 'Processing' URLs on startup. Resetting to 'Pending'.", stuckUrls.Count);
                    foreach (var url in stuckUrls)
                    {
                        url.Status = "Pending";
                    }
                    
                    await _dbWriteLock.WaitAsync(stoppingToken);
                    try
                    {
                        await db.SaveChangesAsync(stoppingToken);
                    }
                    finally
                    {
                        _dbWriteLock.Release();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while resetting stuck Processing URLs or syncing configurations on startup.");
        }

        var runningTasks = new Dictionary<int, Task>();

        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();
                var activeSources = await db.DataSourceConfigs.Where(c => c.IsActive).ToListAsync(stoppingToken);

                foreach (var source in activeSources)
                {
                    if (!runningTasks.ContainsKey(source.Id) || runningTasks[source.Id].IsCompleted)
                    {
                        _logger.LogInformation("Spawning crawler task for source: {SourceName}", source.Name);
                        var task = RunSourceForeverAsync(source, _scraper, stoppingToken);
                        runningTasks[source.Id] = task;
                    }
                }
            }

            await Task.Delay(10000, stoppingToken);
        }
    }

    private async Task RunSourceForeverAsync(DataSourceConfig source, UniversalHtmlScraper scraper, CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Worker>>();

                // Safety gate for local corpus sources: do not enqueue/process until file prep completes.
                var sourceReady = await EnsureSourceReadyForProcessingAsync(source, stoppingToken);
                if (!sourceReady)
                {
                    logger.LogInformation("Source {SourceName} is not ready yet (local file prep incomplete). Retrying in 30s.", source.Name);
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    continue;
                }

                // Ingestion Limit Check: Prevent crawler from running indefinitely on large datasets
                var currentCount = await db.RawUniversalData.CountAsync(d => d.Source == source.Name, stoppingToken);
                var limit = GetMaxRecordsLimit(source);
                if (currentCount >= limit)
                {
                    logger.LogInformation("Source {SourceName} has reached its limit of {Limit} records ({CurrentCount} current). Completing job and cleaning queue.", source.Name, limit, currentCount);
                    
                    var pendingUrls = await db.CrawledUrlQueue
                        .Where(q => q.DataSourceId == source.Id && q.Status == "Pending")
                        .ToListAsync(stoppingToken);
                    if (pendingUrls.Any())
                    {
                        foreach (var pUrl in pendingUrls)
                        {
                            pUrl.Status = "Completed";
                        }
                    }
                    
                    var jobToUpdate = await db.ScrapeJobs.FirstOrDefaultAsync(j => j.SourceName == source.Name, stoppingToken);
                    if (jobToUpdate != null && jobToUpdate.Status != "Completed")
                    {
                        jobToUpdate.Status = "Completed";
                    }
                    
                    await _dbWriteLock.WaitAsync(stoppingToken);
                    try { await db.SaveChangesAsync(stoppingToken); }
                    finally { _dbWriteLock.Release(); }
                    
                    break; // Exit loop for this source
                }

                // 1. Ensure seed URL is in queue if queue is empty
                var hasAny = await db.CrawledUrlQueue.AnyAsync(q => q.DataSourceId == source.Id, stoppingToken);
                if (!hasAny)
                {
                    if (source.Id == 29)
                    {
                        var localPath = source.BaseUrl.Replace("file:///", "").Replace('/', Path.DirectorySeparatorChar);
                        var slangDir = Path.Combine(Path.GetDirectoryName(localPath) ?? @"D:\A_S", "nofal_slang");
                        if (Directory.Exists(slangDir))
                        {
                            var files = Directory.GetFiles(slangDir, "*.xlsx", SearchOption.AllDirectories);
                            foreach (var file in files)
                            {
                                var fileUrl = $"file:///{file.Replace('\\', '/')}";
                                db.CrawledUrlQueue.Add(new CrawledUrl
                                {
                                    DataSourceId = source.Id,
                                    Url = fileUrl,
                                    Status = "Pending",
                                    Depth = 0
                                });
                            }
                        }
                    }
                    else if (source.Id == 50 || source.Id == 51 || source.Id == 52 ||
                             source.Id == 53 || source.Id == 54 || source.Id == 55 || source.Id == 56 || source.Id == 57)
                    {
                        string localPath;
                        string searchPattern;
                        
                        if (source.Id == 50 || source.Id == 51 || source.Id == 52)
                        {
                            localPath = Path.Combine(Path.GetTempPath(), "LisanBits", $"MADAR_{source.Id}");
                            searchPattern = "*.tsv";
                        }
                        else
                        {
                            localPath = Path.Combine(Path.GetTempPath(), "LisanBits", $"Dataset_{source.Id}");
                            searchPattern = source.Id switch
                            {
                                53 => "*.zst",
                                54 => "*.xz",
                                55 => "*.ar",
                                56 => "*.gz",
                                57 => "quran-morphology.txt",
                                _ => "*.*"
                            };
                        }

                        if (Directory.Exists(localPath))
                        {
                            var files = Directory.GetFiles(localPath, searchPattern, SearchOption.AllDirectories);
                            foreach (var file in files)
                            {
                                var fileUrl = $"file:///{file.Replace('\\', '/')}";
                                db.CrawledUrlQueue.Add(new CrawledUrl
                                {
                                    DataSourceId = source.Id,
                                    Url = fileUrl,
                                    Status = "Pending",
                                    Depth = 0
                                });
                            }
                        }
                    }
                    else
                    {
                        db.CrawledUrlQueue.Add(new CrawledUrl
                        {
                            DataSourceId = source.Id,
                            Url = UniversalHtmlScraper.NormalizeUrl(source.BaseUrl),
                            Status = "Pending",
                            Depth = 0
                        });
                    }
                    await _dbWriteLock.WaitAsync(stoppingToken);
                    try { await db.SaveChangesAsync(stoppingToken); }
                    finally { _dbWriteLock.Release(); }
                }

                // 2. Pop next Pending URL
                var pendingUrl = await db.CrawledUrlQueue
                    .Where(q => q.DataSourceId == source.Id && q.Status == "Pending")
                    .OrderBy(q => q.Id)
                    .FirstOrDefaultAsync(stoppingToken);

                if (pendingUrl == null)
                {
                    // Queue is empty — mark as idle but DON'T exit. Sleep and retry so new URLs can be picked up.
                    logger.LogInformation("Queue empty for {SourceName}. Sleeping 60s before retry...", source.Name);
                    var jobToUpdate = await db.ScrapeJobs.FirstOrDefaultAsync(j => j.SourceName == source.Name, stoppingToken);
                    if (jobToUpdate != null && jobToUpdate.Status != "Completed")
                    {
                        jobToUpdate.Status = "Idle";
                        await _dbWriteLock.WaitAsync(stoppingToken);
                        try { await db.SaveChangesAsync(stoppingToken); }
                        finally { _dbWriteLock.Release(); }
                    }
                    await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                    continue;
                }

                // 3. Update ScrapeJob stats
                var job = await db.ScrapeJobs.FirstOrDefaultAsync(j => j.SourceName == source.Name, stoppingToken);
                if (job == null)
                {
                    job = new ScrapeJob
                    {
                        SourceName = source.Name,
                        TargetId = source.Name,
                        LastProcessedIndex = 0,
                        Status = "InProgress",
                        LastUpdatedAt = DateTime.UtcNow
                    };
                    db.ScrapeJobs.Add(job);
                }
                else
                {
                    job.Status = "InProgress";
                    job.LastUpdatedAt = DateTime.UtcNow;
                }
                
                // Track progress by marking it as 'Processing' temporarily
                pendingUrl.Status = "Processing";
                pendingUrl.ProcessedAt = DateTime.UtcNow;
                await _dbWriteLock.WaitAsync(stoppingToken);
                try { await db.SaveChangesAsync(stoppingToken); }
                finally { _dbWriteLock.Release(); }

                logger.LogInformation("Scraping [Depth: {Depth}] {SourceName} at {Url}", pendingUrl.Depth, source.Name, pendingUrl.Url);
                
                // 4. Scrape!
                var result = await scraper.ScrapeAsync(source, pendingUrl.Url, stoppingToken);
                
                int addedData = 0;
                int addedUrls = 0;

                if (result != null)
                {
                    // 4a. Save Target Data
                    foreach (var item in result.ExtractedData)
                    {
                        if (item.Sentences > 0)
                        {
                            bool passes = false;
                            string cleanedText = "";
                            if (source.Category == "Slang" || source.Category == "Dialect")
                            {
                                passes = _dataCleaner.ProcessAndVerifyDialect(item.Text, source.Category, out cleanedText);
                            }
                            else
                            {
                                passes = _dataCleaner.ProcessAndVerify(item.Text, source.Category, out cleanedText);
                            }

                            if (passes)
                            {
                                db.RawUniversalData.Add(new RawUniversalData
                                {
                                    Category = source.Category,
                                    SubContextPath = item.SubContextPath, // resolved taxonomy leaf path from WikiCategoryResolver
                                    Source = source.Name,
                                    TextContent = cleanedText,
                                    SentenceCount = item.Sentences,
                                    WordCount = item.Words,
                                    ScrapedAt = DateTime.UtcNow
                                });
                                addedData++;
                            }
                            else
                            {
                                logger.LogInformation("Text item from source {SourceName} failed quality/deduplication gates. Skipping.", source.Name);
                            }
                        }
                    }

                    // 4a2. Save Lexicon Entries
                    if (result.LexiconEntries != null && result.LexiconEntries.Count > 0)
                    {
                        foreach (var lexiconEntry in result.LexiconEntries)
                        {
                            var exists = await db.LexiconEntries.AnyAsync(l => 
                                l.Word == lexiconEntry.Word && 
                                l.SourceBook == lexiconEntry.SourceBook, 
                                stoppingToken);
                            if (!exists)
                            {
                                if (_dataCleaner.ProcessAndVerifyLexicon(lexiconEntry.Definition, source.Category, out string cleanedDefinition))
                                {
                                    lexiconEntry.Definition = cleanedDefinition;
                                    db.LexiconEntries.Add(lexiconEntry);
                                    addedData++;
                                }
                                else
                                {
                                    logger.LogInformation("Lexicon entry '{Word}' from {SourceBook} failed quality/deduplication gates. Skipping.", lexiconEntry.Word, lexiconEntry.SourceBook);
                                }
                            }
                        }
                    }

                    // 4b. Insert Discovered Links into Queue (BULK check - avoids N+1 queries)
                    if (result.DiscoveredUrls.Count > 0)
                    {
                        var discoveredBatch = new HashSet<string>();
                        foreach (var discoveredUrl in result.DiscoveredUrls)
                        {
                            var normalizedUrl = UniversalHtmlScraper.NormalizeUrl(discoveredUrl);
                            if (!string.IsNullOrWhiteSpace(normalizedUrl))
                            {
                                discoveredBatch.Add(normalizedUrl);
                            }
                        }

                        if (discoveredBatch.Count > 0)
                        {
                            // Load ONLY the matching URLs from the database
                            var discoveredList = discoveredBatch.ToList();
                            var existingUrls = await db.CrawledUrlQueue
                                .Where(q => q.DataSourceId == source.Id && discoveredList.Contains(q.Url))
                                .Select(q => q.Url)
                                .ToHashSetAsync(stoppingToken);

                            var addedThisBatch = new HashSet<string>();
                            foreach (var normalizedUrl in discoveredBatch)
                            {
                                if (!existingUrls.Contains(normalizedUrl) && addedThisBatch.Add(normalizedUrl))
                                {
                                    db.CrawledUrlQueue.Add(new CrawledUrl
                                    {
                                        DataSourceId = source.Id,
                                        Url = normalizedUrl,
                                        Status = "Pending",
                                        Depth = pendingUrl.Depth + 1
                                    });
                                    addedUrls++;
                                }
                            }
                        }
                    }
                }

                // 5. Mark as Completed and Save
                pendingUrl.Status = "Completed";
                job.LastProcessedIndex++;
                
                logger.LogInformation("Extracted {DataCount} items, discovered {UrlCount} links from {Url}", addedData, addedUrls, pendingUrl.Url);

                await _dbWriteLock.WaitAsync(stoppingToken);
                try 
                { 
                    await db.SaveChangesAsync(stoppingToken); 
                }
                catch (DbUpdateException dbEx)
                {
                    logger.LogWarning(dbEx, "DbUpdateException occurred while saving crawl results for {SourceName}. Detaching new entities and saving status...", source.Name);
                    
                    // Detach all added entities to clear the failed state of the change tracker
                    foreach (var entry in db.ChangeTracker.Entries().ToList())
                    {
                        if (entry.State == EntityState.Added)
                        {
                            entry.State = EntityState.Detached;
                        }
                    }
                    
                    // Re-set target URL status to Completed so we skip it next time and avoid infinite loops
                    pendingUrl.Status = "Completed";
                    try
                    {
                        await db.SaveChangesAsync(stoppingToken);
                    }
                    catch (Exception innerEx)
                    {
                        logger.LogError(innerEx, "Failed to save URL completed state. Queue may loop.");
                    }
                }
                finally { _dbWriteLock.Release(); }
                
                // Broadcast Progress to SignalR Dashboard
                if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                {
                    try {
                        await _hubConnection.InvokeAsync("BroadcastProgress", source.Name, job.LastProcessedIndex, stoppingToken);
                        logger.LogDebug("Broadcasted progress for {SourceName} at index {LastProcessedIndex}.", source.Name, job.LastProcessedIndex);
                    } catch (Exception ex) {
                        logger.LogWarning(ex, "Failed to broadcast progress.");
                    }
                }

                // Be polite to servers, wait 1 second
                await Task.Delay(1000, stoppingToken); 
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in dedicated scraper loop for Source: {SourceName}", source.Name);
        }
    }

    // -------------------------------------------------------------------------
    // Slang dataset helpers
    // -------------------------------------------------------------------------


    private async Task DownloadAndExtractSlangDatasetAsync(string targetDir, string zipPath, string kaggleUrl, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);

        _logger.LogInformation("Starting Kaggle slang dataset download to {Path}...", zipPath);
        if (!await DownloadKaggleZipAsync(kaggleUrl, zipPath, ct))
        {
            return;
        }

        _logger.LogInformation("Download complete. Extracting {ZipPath} to {TargetDir}...", zipPath, targetDir);

        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);
        
        try { File.Delete(zipPath); } catch { }
        
        _logger.LogInformation("Slang dataset extracted to {TargetDir}.", targetDir);
    }


    private async Task DownloadExtractAndFixArbEgyCmpAsync(string correctedPath, string kaggleUrl, CancellationToken ct)
    {
        var zipPath = Path.ChangeExtension(correctedPath, ".zip");
        var extractDir = Path.Combine(Path.GetDirectoryName(correctedPath) ?? Path.GetTempPath(), "arb-egy-cmp-raw");
        Directory.CreateDirectory(extractDir);

        _logger.LogInformation("Starting ARB-EGY-CMP download to {Path}...", zipPath);
        if (!await DownloadKaggleZipAsync(kaggleUrl, zipPath, ct))
        {
            return;
        }

        _logger.LogInformation("Extracting ARB-EGY-CMP from {ZipPath}...", zipPath);
        if (Directory.Exists(extractDir))
        {
            Directory.Delete(extractDir, recursive: true);
            Directory.CreateDirectory(extractDir);
        }
        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
        File.Delete(zipPath);

        var jsonCandidates = Directory.GetFiles(extractDir, "*.json", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(extractDir, "*.jsonl", SearchOption.AllDirectories))
            .ToArray();

        if (jsonCandidates.Length == 0)
        {
            _logger.LogWarning("No JSON/JSONL files found in extracted ARB-EGY-CMP directory {Dir}.", extractDir);
            return;
        }

        var sourceJson = jsonCandidates[0];
        _logger.LogInformation("Fixing ARB-EGY-CMP encoding from {Source} to {Target}", sourceJson, correctedPath);
        await FixJsonCorpusToReadableArabicAsync(sourceJson, correctedPath, ct);
        _logger.LogInformation("ARB-EGY-CMP corrected file ready at {Path}.", correctedPath);
    }

    private async Task<bool> EnsureSourceReadyForProcessingAsync(DataSourceConfig source, CancellationToken ct)
    {
        if (source.Id != 29 && source.Id != 30 && source.Id != 50 && source.Id != 51 && source.Id != 52 &&
            source.Id != 53 && source.Id != 54 && source.Id != 55 && source.Id != 56 && source.Id != 57)
            return true;

        string localPath;
        if (source.Id == 50 || source.Id == 51 || source.Id == 52)
        {
            localPath = Path.Combine(Path.GetTempPath(), "LisanBits", $"MADAR_{source.Id}").Replace('\\', '/');
        }
        else if (source.Id == 53 || source.Id == 54 || source.Id == 55 || source.Id == 56 || source.Id == 57)
        {
            localPath = Path.Combine(Path.GetTempPath(), "LisanBits", $"Dataset_{source.Id}").Replace('\\', '/');
        }
        else
        {
            if (!source.BaseUrl.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                return true;
            localPath = source.BaseUrl.Replace("file:///", "").Replace('/', Path.DirectorySeparatorChar);
        }
        
        if (source.Id == 29)
        {
            var slangDir = Path.Combine(Path.GetDirectoryName(localPath) ?? @"D:\A_S", "nofal_slang");
            if (Directory.Exists(slangDir) && Directory.GetFiles(slangDir, "*.xlsx", SearchOption.AllDirectories).Length > 0)
                return true;
        }
        else if (source.Id == 50 || source.Id == 51 || source.Id == 52)
        {
            if (Directory.Exists(localPath) && Directory.GetFiles(localPath, "*.tsv", SearchOption.AllDirectories).Length > 0)
                return true;
        }
        else if (source.Id == 55)
        {
            if (Directory.Exists(localPath) && Directory.GetFiles(localPath, "*.ar", SearchOption.AllDirectories).Length > 0)
                return true;
        }
        else if (source.Id == 53)
        {
            if (File.Exists(Path.Combine(localPath, "ar_meta_part_1.jsonl.zst")))
                return true;
        }
        else if (source.Id == 54)
        {
            if (File.Exists(Path.Combine(localPath, "ar.txt.xz")))
                return true;
        }
        else if (source.Id == 56)
        {
            if (File.Exists(Path.Combine(localPath, "ar.txt.gz")))
                return true;
        }
        else if (source.Id == 57)
        {
            if (File.Exists(Path.Combine(localPath, "quran-morphology.txt")))
                return true;
        }
        else
        {
            if (File.Exists(localPath))
                return true;
        }

        await _datasetPrepLock.WaitAsync(ct);
        try
        {
            if (source.Id == 29)
            {
                var slangDir = Path.Combine(Path.GetDirectoryName(localPath) ?? @"D:\A_S", "nofal_slang");
                if (Directory.Exists(slangDir) && Directory.GetFiles(slangDir, "*.xlsx", SearchOption.AllDirectories).Length > 0)
                    return true;

                var zipPath = Path.Combine(Path.GetDirectoryName(localPath) ?? @"D:\A_S", "two-million-rows-egyptian-datasets.zip");
                var kaggleUrl = _configuration["SlangDataset:KaggleUrl"]
                    ?? "https://www.kaggle.com/api/v1/datasets/download/mostafanofal/two-million-rows-egyptian-datasets";
                await DownloadAndExtractSlangDatasetAsync(slangDir, zipPath, kaggleUrl, ct);
            }
            else if (source.Id == 30)
            {
                var kaggleUrl = _configuration["SlangDataset:CmpKaggleUrl"]
                    ?? "https://www.kaggle.com/api/v1/datasets/download/mksaad/arb-egy-cmp-corpus";
                await DownloadExtractAndFixArbEgyCmpAsync(localPath, kaggleUrl, ct);
            }
            else if (source.Id == 50 || source.Id == 51 || source.Id == 52)
            {
                await DownloadAndExtractMadarZipAsync(localPath, source.BaseUrl, ct);
            }
            else if (source.Id == 53)
            {
                await DownloadDatasetFileAsync(localPath, "ar_meta_part_1.jsonl.zst", source.BaseUrl, ct);
            }
            else if (source.Id == 54)
            {
                await DownloadDatasetFileAsync(localPath, "ar.txt.xz", source.BaseUrl, ct);
            }
            else if (source.Id == 55)
            {
                await DownloadAndExtractZipAsync(localPath, source.BaseUrl, ct);
            }
            else if (source.Id == 56)
            {
                await DownloadDatasetFileAsync(localPath, "ar.txt.gz", source.BaseUrl, ct);
            }
            else if (source.Id == 57)
            {
                await DownloadDatasetFileAsync(localPath, "quran-morphology.txt", source.BaseUrl, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed preparing local dataset for source {SourceName}.", source.Name);
        }
        finally
        {
            _datasetPrepLock.Release();
        }

        if (source.Id == 29)
        {
            var slangDir = Path.Combine(Path.GetDirectoryName(localPath) ?? @"D:\A_S", "nofal_slang");
            return Directory.Exists(slangDir) && Directory.GetFiles(slangDir, "*.xlsx", SearchOption.AllDirectories).Length > 0;
        }
        if (source.Id == 50 || source.Id == 51 || source.Id == 52)
        {
            return Directory.Exists(localPath) && Directory.GetFiles(localPath, "*.tsv", SearchOption.AllDirectories).Length > 0;
        }
        if (source.Id == 55)
        {
            return Directory.Exists(localPath) && Directory.GetFiles(localPath, "*.ar", SearchOption.AllDirectories).Length > 0;
        }
        if (source.Id == 53)
        {
            return File.Exists(Path.Combine(localPath, "ar_meta_part_1.jsonl.zst"));
        }
        if (source.Id == 54)
        {
            return File.Exists(Path.Combine(localPath, "ar.txt.xz"));
        }
        if (source.Id == 56)
        {
            return File.Exists(Path.Combine(localPath, "ar.txt.gz"));
        }
        if (source.Id == 57)
        {
            return File.Exists(Path.Combine(localPath, "quran-morphology.txt"));
        }
        return File.Exists(localPath);
    }

    private async Task DownloadAndExtractMadarZipAsync(string targetDir, string url, CancellationToken ct)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
        _logger.LogInformation("Downloading MADAR archive from {Url} to {ZipPath}...", url, zipPath);
        
        try
        {
            if (!await DownloadKaggleZipAsync(url, zipPath, ct))
            {
                _logger.LogError("Failed to download MADAR archive from {Url}", url);
                return;
            }

            _logger.LogInformation("Extracting MADAR archive to {TargetDir}...", targetDir);
            Directory.CreateDirectory(targetDir);

            using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.Contains('\r') || entry.FullName.Contains('\n'))
                {
                    _logger.LogWarning("Skipping zip entry with invalid character: {EntryName}", entry.FullName);
                    continue;
                }

                var destinationPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
                if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                var entryDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(entryDir))
                {
                    Directory.CreateDirectory(entryDir);
                }
                entry.ExtractToFile(destinationPath, overwrite: true);
            }

            _logger.LogInformation("Extraction of MADAR archive completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during download and extraction of MADAR archive from {Url}", url);
            throw;
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
        }
    }

    private async Task<bool> DownloadKaggleZipAsync(string kaggleUrl, string zipPath, CancellationToken ct)
    {
        using var anonymousClient = new HttpClient();
        anonymousClient.Timeout = TimeSpan.FromMinutes(30);

        // First try direct download (works for public cURL-style URLs).
        var anonymousResponse = await anonymousClient.GetAsync(kaggleUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        if (anonymousResponse.IsSuccessStatusCode)
        {
            await using var anonymousFileStream = File.Create(zipPath);
            await anonymousResponse.Content.CopyToAsync(anonymousFileStream, ct);
            return true;
        }

        // If endpoint requires auth, retry with Kaggle credentials.
        if (anonymousResponse.StatusCode != System.Net.HttpStatusCode.Unauthorized &&
            anonymousResponse.StatusCode != System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("Dataset download failed without auth. Status: {StatusCode}", anonymousResponse.StatusCode);
            return false;
        }

        var kaggleUsername = Environment.GetEnvironmentVariable("KAGGLE_USERNAME") ?? string.Empty;
        var kaggleKey = Environment.GetEnvironmentVariable("KAGGLE_KEY") ?? string.Empty;

        if (string.IsNullOrEmpty(kaggleUsername) || string.IsNullOrEmpty(kaggleKey))
        {
            var kaggleJsonPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kaggle", "kaggle.json");

            if (File.Exists(kaggleJsonPath))
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(kaggleJsonPath, ct));
                kaggleUsername = doc.RootElement.GetProperty("username").GetString() ?? string.Empty;
                kaggleKey = doc.RootElement.GetProperty("key").GetString() ?? string.Empty;
            }
        }

        if (string.IsNullOrEmpty(kaggleUsername) || string.IsNullOrEmpty(kaggleKey))
        {
            _logger.LogWarning("Dataset endpoint requires authentication, but Kaggle credentials were not found.");
            return false;
        }

        using var authClient = new HttpClient();
        authClient.Timeout = TimeSpan.FromMinutes(30);
        var credentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{kaggleUsername}:{kaggleKey}"));
        authClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

        var authResponse = await authClient.GetAsync(kaggleUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!authResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Dataset download failed with auth. Status: {StatusCode}", authResponse.StatusCode);
            return false;
        }

        await using var authFileStream = File.Create(zipPath);
        await authResponse.Content.CopyToAsync(authFileStream, ct);
        return true;
    }

    private static async Task FixJsonCorpusToReadableArabicAsync(string inputPath, string outputPath, CancellationToken ct)
    {
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var tempOutputPath = outputPath + ".tmp";

        // Delete temp file if it exists from a previous failed attempt
        if (File.Exists(tempOutputPath))
        {
            try { File.Delete(tempOutputPath); } catch { }
        }

        // 1. Handles JSONL (no array braces []) by reading line-by-line
        try
        {
            await using (var inStream = File.OpenRead(inputPath))
            using (var reader = new StreamReader(inStream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 65536))
            await using (var outStream = File.Open(tempOutputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(outStream, new System.Text.UTF8Encoding(false), 65536))
            {
                // 3. Fix Arabic Encoding: UnsafeRelaxedJsonEscaping prevents \u0623 style escaping
                // and writes raw, readable UTF-8 Arabic characters instead.
                var jsonOptions = new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null)
                    {
                        break;
                    }

                    var trimmed = line.Trim();

                    // Ignores stray array braces if they exist, but naturally processes raw JSONL 
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "[" || trimmed == "]")
                    {
                        continue;
                    }

                    if (trimmed.EndsWith(","))
                    {
                        trimmed = trimmed[..^1];
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(trimmed);
                        var root = doc.RootElement;

                        var text = root.TryGetProperty("text", out var textProp)
                            ? (textProp.GetString() ?? string.Empty)
                            : string.Empty;
                        var filename = root.TryGetProperty("filename", out var fileProp)
                            ? (fileProp.GetString() ?? string.Empty)
                            : string.Empty;
                        var label = root.TryGetProperty("label", out var labelProp)
                            ? (labelProp.GetString() ?? string.Empty)
                            : "ar";

                        var fixedObj = new
                        {
                            text,
                            filename,
                            label
                        };

                        await writer.WriteLineAsync(JsonSerializer.Serialize(fixedObj, jsonOptions));
                    }
                    catch
                    {
                        // Best effort: keep original line if parse fails.
                        await writer.WriteLineAsync(line);
                    }
                }

                await writer.FlushAsync(ct);
            }

            // Streams are fully disposed at this point
            // Delete old output if it exists
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            // Move temp to final location with retry logic for Windows file locking
            int retries = 0;
            while (retries < 3)
            {
                try
                {
                    File.Move(tempOutputPath, outputPath, overwrite: true);
                    break;
                }
                catch (IOException) when (retries < 2)
                {
                    retries++;
                    await Task.Delay(500);
                }
            }
        }
        catch
        {
            // Clean up temp file on any error
            if (File.Exists(tempOutputPath))
            {
                try { File.Delete(tempOutputPath); } catch { }
            }
            throw;
        }
    }

    private async Task DownloadDatasetFileAsync(string targetDir, string fileName, string url, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);
        var targetFile = Path.Combine(targetDir, fileName);
        _logger.LogInformation("Downloading {FileName} from {Url} to {TargetFile}...", fileName, url, targetFile);
        
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromHours(2);
        
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        
        await using var fileStream = File.Create(targetFile);
        await response.Content.CopyToAsync(fileStream, ct);
        _logger.LogInformation("Finished downloading {FileName}.", fileName);
    }

    private async Task DownloadAndExtractZipAsync(string targetDir, string url, CancellationToken ct)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
        _logger.LogInformation("Downloading zip from {Url} to {ZipPath}...", url, zipPath);
        
        try
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromHours(2);
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await using var fileStream = File.Create(zipPath);
                await response.Content.CopyToAsync(fileStream, ct);
            }

            _logger.LogInformation("Extracting zip to {TargetDir}...", targetDir);
            Directory.CreateDirectory(targetDir);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);
            _logger.LogInformation("Finished extracting zip to {TargetDir}.", targetDir);
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
        }
    }

    private int GetMaxRecordsLimit(DataSourceConfig source)
    {
        // Limits the number of records (rows in RawUniversalData) per source.
        // Dictionaries, grammar, Quran, and Hadith are fully ingested (int.MaxValue).
        return source.Id switch
        {
            // Web crawlers (limit pages)
            9 => 5000,   // Wikipedia Science
            10 => 5000,  // Hsoub Academy
            15 => 10000, // Altibbi
            16 => 5000,  // Wikipedia Medicine
            17 => 5000,  // Investing.com
            18 => 5000,  // Al-Eqtisad
            21 => 10000, // Kooora
            25 => 10000, // Youm7
            26 => 10000, // Masrawy
            27 => 5000,  // Wikipedia Literature
            28 => 5000,  // Wikipedia DailyLife
            36 => 5000,  // Wikipedia Physics
            37 => 5000,  // Wikipedia Chemistry
            38 => 5000,  // Wikipedia Biology
            39 => 5000,  // Wikipedia Mathematics
            40 => 5000,  // Wikipedia Cardiology
            41 => 5000,  // Wikipedia Neurology
            42 => 5000,  // Wikipedia Economics
            43 => 5000,  // Wikipedia Poetry
            44 => 5000,  // Wikipedia Food
            45 => 5000,  // Wikipedia Fiqh

            // Massive offline corpora (limit rows/sentences to prevent domain imbalance)
            53 => 50000,  // OSCAR Arabic
            54 => 50000,  // CC-100 Arabic
            55 => 100000, // OPUS Parallel Corpus
            56 => 150000, // OpenSubtitles (Arabic)

            // Dictionaries, Religion, and Grammar have no limits
            _ => int.MaxValue
        };
    }
}
