using System.Text;
using System.Text.Json;
using Neo4j.Driver;
using Microsoft.ML;

namespace LisanBits.GrammarPipeline;

public enum GrammarPipelineStatus
{
    Idle,
    Ingesting,
    Mining,
    Training,
    Completed,
    Failed
}

public class GrammarManagerService
{
    private readonly IDriver _driver;
    private readonly ILogger<GrammarManagerService> _logger;
    private readonly string _treebankPath;
    private readonly string _rulesOutputPath;
    private readonly string _modelOutputPath;
    private readonly string[] _downloadUrls;
    
    private readonly object _lock = new();
    private Task? _activeTask;

    public GrammarPipelineStatus Status { get; private set; } = GrammarPipelineStatus.Idle;
    public double Progress { get; private set; }
    public long ProcessedItems { get; private set; }
    public long TotalItems { get; private set; }
    public long MergedNodes { get; private set; }
    public long MergedEdges { get; private set; }
    public double ModelAccuracy { get; private set; }
    public string? ErrorMessage { get; private set; }
    public List<string> Logs { get; } = new();

    public GrammarManagerService(IDriver driver, IConfiguration configuration, ILogger<GrammarManagerService> logger)
    {
        _driver = driver;
        _logger = logger;

        // Resolve absolute or relative path for Treebank CSV
        var treebankConfigPath = configuration["GrammarPipeline:TreebankPath"];
        if (string.IsNullOrEmpty(treebankConfigPath))
        {
            throw new InvalidOperationException("Configuration value 'GrammarPipeline:TreebankPath' is missing.");
        }

        if (Path.IsPathRooted(treebankConfigPath))
        {
            _treebankPath = treebankConfigPath;
        }
        else
        {
            _treebankPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../", treebankConfigPath));
        }

        // Rules and model destination paths in Dashboard wwwroot
        var dashboardRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../src/LisanBits.Dashboard/wwwroot"));
        Directory.CreateDirectory(dashboardRoot);

        _rulesOutputPath = Path.Combine(dashboardRoot, "grammar_rules.json");
        _modelOutputPath = Path.Combine(dashboardRoot, "grammar_tagger.zip");

        _downloadUrls = configuration.GetSection("GrammarPipeline:DownloadUrls")
            .GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrEmpty(v))
            .ToArray()!;

        if (_downloadUrls.Length == 0)
        {
            throw new InvalidOperationException("Configuration values under 'GrammarPipeline:DownloadUrls' are missing or empty.");
        }

        AddLog($"Grammar Pipeline configured.");
        AddLog($"Treebank path: {_treebankPath}");
        AddLog($"Rules output: {_rulesOutputPath}");
        AddLog($"Model output: {_modelOutputPath}");
    }

    private void AddLog(string msg)
    {
        var log = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        _logger.LogInformation("{LogMsg}", log);
        lock (Logs)
        {
            Logs.Add(log);
            if (Logs.Count > 100) Logs.RemoveAt(0);
        }
    }

    public bool StartIngest()
    {
        lock (_lock)
        {
            if (Status != GrammarPipelineStatus.Idle && Status != GrammarPipelineStatus.Completed && Status != GrammarPipelineStatus.Failed)
                return false;

            Status = GrammarPipelineStatus.Ingesting;
            Progress = 0;
            ProcessedItems = 0;
            TotalItems = 0;
            MergedNodes = 0;
            MergedEdges = 0;
            ErrorMessage = null;
            Logs.Clear();

            _activeTask = Task.Run(RunIngestAsync);
            return true;
        }
    }

    public bool StartMine()
    {
        lock (_lock)
        {
            if (Status != GrammarPipelineStatus.Idle && Status != GrammarPipelineStatus.Completed && Status != GrammarPipelineStatus.Failed)
                return false;

            Status = GrammarPipelineStatus.Mining;
            Progress = 0;
            ProcessedItems = 0;
            TotalItems = 0;
            ErrorMessage = null;
            Logs.Clear();

            _activeTask = Task.Run(RunMineAsync);
            return true;
        }
    }

    public bool StartTrain()
    {
        lock (_lock)
        {
            if (Status != GrammarPipelineStatus.Idle && Status != GrammarPipelineStatus.Completed && Status != GrammarPipelineStatus.Failed)
                return false;

            Status = GrammarPipelineStatus.Training;
            Progress = 0;
            ProcessedItems = 0;
            TotalItems = 0;
            ModelAccuracy = 0;
            ErrorMessage = null;
            Logs.Clear();

            _activeTask = Task.Run(RunTrainAsync);
            return true;
        }
    }

    public async Task ResetGrammarAsync()
    {
        lock (_lock)
        {
            if (Status != GrammarPipelineStatus.Idle && Status != GrammarPipelineStatus.Completed && Status != GrammarPipelineStatus.Failed)
                throw new InvalidOperationException("Cannot reset database while operations are running.");
        }

        AddLog("Resetting Grammar nodes and edges in Neo4j...");
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            AddLog("Removing GOVERNS relationships...");
            await tx.RunAsync("MATCH ()-[r:GOVERNS]->() DETACH DELETE r");
            
            AddLog("Clearing case/mood metadata from Word nodes...");
            await tx.RunAsync("MATCH (w:Word) REMOVE w.case, w.mood, w.pos, w.lemma");
        });
        AddLog("Grammar data successfully reset.");
    }

    private async Task RunIngestAsync()
    {
        try
        {
            AddLog("Starting Quranic Treebank Ingestion...");

            if (!File.Exists(_treebankPath))
            {
                AddLog("Quranic.csv not found locally. Preparing to download treebank archive...");
                var dir = Path.GetDirectoryName(_treebankPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var rarPath = Path.Combine(dir ?? ".", "Quranic.rar");
                var downloadSuccess = false;

                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromMinutes(10);

                foreach (var url in _downloadUrls)
                {
                    try
                    {
                        AddLog($"Downloading treebank from {url}...");
                        using var response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                        if (!response.IsSuccessStatusCode)
                        {
                            AddLog($"URL returned status: {response.StatusCode}. Trying next...");
                            continue;
                        }

                        var totalBytes = response.Content.Headers.ContentLength ?? 0;
                        await using var stream = await response.Content.ReadAsStreamAsync();
                        await using var outputRarStream = new FileStream(rarPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                        var buffer = new byte[81920];
                        long downloadedBytes = 0;
                        int bytesRead;
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await outputRarStream.WriteAsync(buffer, 0, bytesRead);
                            downloadedBytes += bytesRead;
                            if (totalBytes > 0)
                            {
                                Progress = Math.Round((double)downloadedBytes / totalBytes * 100, 2);
                            }
                        }

                        downloadSuccess = true;
                        AddLog("Download completed successfully.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        AddLog($"Error downloading from {url}: {ex.Message}");
                    }
                }

                if (!downloadSuccess)
                {
                    throw new InvalidOperationException("Failed to download Quranic.rar from the public repository branches.");
                }

                // Extract RAR using native tar
                AddLog("Extracting Quranic.csv using native tar...");
                try
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "tar",
                        Arguments = $"-xf \"{rarPath}\" -C \"{dir}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = System.Diagnostics.Process.Start(startInfo);
                    if (process == null)
                    {
                        throw new InvalidOperationException("Failed to start native tar process.");
                    }

                    await process.WaitForExitAsync();
                    if (process.ExitCode != 0)
                    {
                        var error = await process.StandardError.ReadToEndAsync();
                        throw new InvalidOperationException($"tar extraction failed with exit code {process.ExitCode}. Error: {error}");
                    }

                    AddLog("Extraction completed successfully.");
                    
                    // Clean up RAR file
                    if (File.Exists(rarPath))
                    {
                        File.Delete(rarPath);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to extract Quranic.rar: {ex.Message}");
                }
            }

            // Ensure constraints are set
            await using (var session = _driver.AsyncSession())
            {
                await session.ExecuteWriteAsync(async tx =>
                {
                    await tx.RunAsync("CREATE CONSTRAINT root_text_unique IF NOT EXISTS FOR (r:Root) REQUIRE r.text IS UNIQUE");
                    await tx.RunAsync("CREATE CONSTRAINT word_text_unique IF NOT EXISTS FOR (w:Word) REQUIRE w.text IS UNIQUE");
                });
            }

            AddLog("Neo4j database unique constraints verified.");

            // Determine line count for progress calculation (approximate or quick read)
            AddLog("Scanning treebank file...");
            long lineCount = 0;
            using (var reader = new StreamReader(_treebankPath, Encoding.Unicode))
            {
                while (await reader.ReadLineAsync() != null)
                {
                    lineCount++;
                }
            }
            TotalItems = lineCount - 1; // subtract header
            AddLog($"Treebank size: {TotalItems:N0} records.");

            using var fileStream = new FileStream(_treebankPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var fileReader = new StreamReader(fileStream, Encoding.Unicode);

            var header = await fileReader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(header))
            {
                throw new InvalidOperationException("Treebank file is empty or missing headers.");
            }

            var wordsBatch = new List<object>();
            var relationsBatch = new List<object>();
            var batchSize = 1000;

            string? line;
            while ((line = await fileReader.ReadLineAsync()) != null)
            {
                ProcessedItems++;
                if (ProcessedItems % 20000 == 0)
                {
                    Progress = Math.Round((double)ProcessedItems / TotalItems * 100, 2);
                    AddLog($"Processed {ProcessedItems:N0} records. Merged {MergedNodes:N0} Word/Root nodes, {MergedEdges:N0} GOVERNS edges...");
                }

                var parts = line.Split('\t');
                if (parts.Length < 44) continue;

                var word = parts[12].Trim(); // imlaai_token
                if (string.IsNullOrEmpty(word) || word == "(*)" || word == "_")
                {
                    word = parts[10].Trim(); // fallback to uthmani_token
                }
                if (string.IsNullOrEmpty(word) || word == "(*)" || word == "_")
                {
                    continue; // skip sentence boundaries/empty rows
                }

                var root = parts[25].Trim(); // root_ar
                var lemma = parts[23].Trim(); // lemma_ar
                var pos = parts[16].Trim(); // pos
                
                // Normalise case and mood labels
                var caseRaw = parts[33].Trim().ToUpperInvariant(); // nominal_case
                var caseNormalized = caseRaw switch
                {
                    "NOM" => "Nominative",
                    "ACC" => "Accusative",
                    "GEN" => "Genitive",
                    _ => ""
                };

                var moodRaw = parts[31].Trim().ToUpperInvariant(); // verb_mood
                var moodNormalized = moodRaw switch
                {
                    "IND" => "Indicative",
                    "SUBJ" => "Subjunctive",
                    "JUS" => "Jussive",
                    _ => ""
                };

                wordsBatch.Add(new
                {
                    Word = word,
                    Root = root == "ـ" ? "" : root,
                    Lemma = lemma == "ـ" ? "" : lemma,
                    Pos = pos,
                    Case = caseNormalized,
                    Mood = moodNormalized
                });

                // Dependency relationship parameters
                var relLabel = parts[40].Trim(); // rel_label
                var relLabelAr = parts[41].Trim(); // rel_label_ar
                var refTokenIdRaw = parts[42].Trim(); // ref_token_id

                // We only link if it specifies a valid governing target token ID
                if (!string.IsNullOrEmpty(refTokenIdRaw) && int.TryParse(refTokenIdRaw, out var refTokenId) && refTokenId > 0)
                {
                    var sura = int.Parse(parts[6].Trim());
                    var aya = int.Parse(parts[7].Trim());
                    var sentenceId = sura * 10000 + aya;
                    var tid = int.Parse(parts[8].Trim()); // word index within the verse

                    relationsBatch.Add(new
                    {
                        SentenceId = sentenceId,
                        Tid = tid,
                        RefTid = refTokenId,
                        WordText = word,
                        Role = relLabel,
                        RoleAr = relLabelAr
                    });
                }

                if (wordsBatch.Count >= batchSize)
                {
                    await FlushWordsBatchAsync(wordsBatch);
                    MergedNodes += wordsBatch.Count;
                    wordsBatch.Clear();
                }
            }

            // Flush remaining words
            if (wordsBatch.Count > 0)
            {
                await FlushWordsBatchAsync(wordsBatch);
                MergedNodes += wordsBatch.Count;
                wordsBatch.Clear();
            }

            AddLog("Completed inserting all Word and Root nodes. Now indexing grammatical dependencies ([:GOVERNS])...");

            // To map relations correctly, we need to know the mapping of Tid -> Word in each sentence.
            // Let's resolve the links in batches. 
            // In Neo4j, we can merge relations by matching the words from the same sentence.
            // Let's process the relations batch.
            var sentenceGroups = relationsBatch
                .Cast<dynamic>()
                .GroupBy(r => (int)r.SentenceId)
                .ToList();

            TotalItems = sentenceGroups.Count;
            ProcessedItems = 0;
            Progress = 0;

            var relationsMergeBatch = new List<object>();

            foreach (var group in sentenceGroups)
            {
                ProcessedItems++;
                if (ProcessedItems % 2000 == 0)
                {
                    Progress = Math.Round((double)ProcessedItems / TotalItems * 100, 2);
                    AddLog($"Linked dependencies for {ProcessedItems:N0} of {TotalItems:N0} sentences...");
                }

                // Map Tid -> WordText for this sentence
                var tokenMap = new Dictionary<int, string>();
                foreach (var g in group)
                {
                    tokenMap[(int)g.Tid] = (string)g.WordText;
                }

                foreach (var rel in group)
                {
                    if (tokenMap.TryGetValue((int)rel.RefTid, out string? govText))
                    {
                        relationsMergeBatch.Add(new
                        {
                            GovText = govText,
                            GovdText = (string)rel.WordText,
                            Role = (string)rel.Role,
                            RoleAr = (string)rel.RoleAr
                        });
                    }
                }

                if (relationsMergeBatch.Count >= batchSize)
                {
                    await FlushRelationsBatchAsync(relationsMergeBatch);
                    MergedEdges += relationsMergeBatch.Count;
                    relationsMergeBatch.Clear();
                }
            }

            if (relationsMergeBatch.Count > 0)
            {
                await FlushRelationsBatchAsync(relationsMergeBatch);
                MergedEdges += relationsMergeBatch.Count;
                relationsMergeBatch.Clear();
            }

            Status = GrammarPipelineStatus.Completed;
            Progress = 100;
            AddLog($"Successfully ingested Quranic treebank. Merged {MergedNodes:N0} nodes and {MergedEdges:N0} relationships.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed Quranic Treebank ingestion.");
            Status = GrammarPipelineStatus.Failed;
            ErrorMessage = ex.Message;
            AddLog($"Ingest failed: {ex.Message}");
        }
    }

    private async Task FlushWordsBatchAsync(List<object> batch)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var query = @"
                UNWIND $batch AS row
                MERGE (w:Word {text: row.Word})
                SET w.pos = row.Pos, w.lemma = row.Lemma, w.case = row.Case, w.mood = row.Mood
                WITH w, row
                WHERE row.Root IS NOT NULL AND row.Root <> ''
                MERGE (r:Root {text: row.Root})
                MERGE (w)-[:DERIVED_FROM]->(r)
            ";
            await tx.RunAsync(query, new { batch });
        });
    }

    private async Task FlushRelationsBatchAsync(List<object> batch)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var query = @"
                UNWIND $batch AS row
                MATCH (gov:Word {text: row.GovText})
                MATCH (govd:Word {text: row.GovdText})
                MERGE (gov)-[r:GOVERNS {role: row.Role, role_ar: row.RoleAr}]->(govd)
            ";
            await tx.RunAsync(query, new { batch });
        });
    }

    private async Task RunMineAsync()
    {
        try
        {
            AddLog("Running Statistical Pattern Miner on Neo4j...");

            await using var session = _driver.AsyncSession();
            
            AddLog("Querying dependency case alignments...");
            var query = @"
                MATCH (gov:Word)-[r:GOVERNS]->(govd:Word)
                WHERE govd.case IS NOT NULL AND govd.case <> ''
                RETURN gov.text AS GovWord, gov.pos AS GovPos, r.role AS Relation, govd.case AS GovernedCase, count(*) AS Occurrences
                ORDER BY Occurrences DESC
            ";

            var wordRules = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var posRules = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            long rulesMined = 0;

            await session.ExecuteReadAsync(async tx =>
            {
                var result = await tx.RunAsync(query);
                while (await result.FetchAsync())
                {
                    var govWord = result.Current["GovWord"].As<string>();
                    var govPos = result.Current["GovPos"].As<string>();
                    var relation = result.Current["Relation"].As<string>();
                    var governedCase = result.Current["GovernedCase"].As<string>();

                    // Word rules
                    if (!string.IsNullOrEmpty(govWord))
                    {
                        if (!wordRules.TryGetValue(govWord, out var relDict))
                        {
                            relDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            wordRules[govWord] = relDict;
                        }
                        // Default to the most frequent case for this specific relation
                        if (!relDict.ContainsKey(relation))
                        {
                            relDict[relation] = governedCase;
                            rulesMined++;
                        }
                    }

                    // POS-level rules
                    if (!string.IsNullOrEmpty(govPos))
                    {
                        if (!posRules.TryGetValue(govPos, out var relDict))
                        {
                            relDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            posRules[govPos] = relDict;
                        }
                        if (!relDict.ContainsKey(relation))
                        {
                            relDict[relation] = governedCase;
                            rulesMined++;
                        }
                    }
                }
            });

            AddLog($"Extracted {rulesMined:N0} statistical grammatical rule triggers.");

            var schema = new
            {
                GeneratedAt = DateTime.UtcNow,
                WordRules = wordRules,
                PosRules = posRules
            };

            var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_rulesOutputPath, json, Encoding.UTF8);

            AddLog($"Grammar rules successfully saved to: {_rulesOutputPath}");

            Status = GrammarPipelineStatus.Completed;
            Progress = 100;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed pattern mining.");
            Status = GrammarPipelineStatus.Failed;
            ErrorMessage = ex.Message;
            AddLog($"Mining failed: {ex.Message}");
        }
    }

    private async Task RunTrainAsync()
    {
        try
        {
            AddLog("Preparing context windows from Quranic Treebank...");

            if (!File.Exists(_treebankPath))
            {
                throw new FileNotFoundException($"Quranic treebank file not found at {_treebankPath}.");
            }

            var tokenDataList = new List<GrammarTokenData>();
            var sentenceTokens = new List<RawToken>();

            using (var fileStream = new FileStream(_treebankPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var fileReader = new StreamReader(fileStream, Encoding.Unicode))
            {
                var header = await fileReader.ReadLineAsync();
                string? line;
                int currentSentenceId = -1;

                while ((line = await fileReader.ReadLineAsync()) != null)
                {
                    var parts = line.Split('\t');
                    if (parts.Length < 44) continue;

                    var sentenceId = int.Parse(parts[2]);
                    var word = parts[12].Trim(); // imlaai_token
                    if (string.IsNullOrEmpty(word) || word == "(*)" || word == "_")
                    {
                        word = parts[10].Trim();
                    }
                    if (string.IsNullOrEmpty(word) || word == "(*)" || word == "_")
                    {
                        continue;
                    }

                    var pos = parts[16].Trim();
                    var lemma = parts[23].Trim();
                    
                    var caseRaw = parts[33].Trim().ToUpperInvariant();
                    var caseVal = caseRaw switch { "NOM" => "Nominative", "ACC" => "Accusative", "GEN" => "Genitive", _ => "" };
                    
                    var moodRaw = parts[31].Trim().ToUpperInvariant();
                    var moodVal = moodRaw switch { "IND" => "Indicative", "SUBJ" => "Subjunctive", "JUS" => "Jussive", _ => "" };

                    var label = !string.IsNullOrEmpty(caseVal) ? caseVal : (!string.IsNullOrEmpty(moodVal) ? moodVal : "");

                    if (sentenceId != currentSentenceId)
                    {
                        if (sentenceTokens.Count > 0)
                        {
                            ExtractContextWindows(sentenceTokens, tokenDataList);
                            sentenceTokens.Clear();
                        }
                        currentSentenceId = sentenceId;
                    }

                    sentenceTokens.Add(new RawToken
                    {
                        Word = word,
                        Pos = pos,
                        Lemma = lemma,
                        Label = label
                    });
                }

                if (sentenceTokens.Count > 0)
                {
                    ExtractContextWindows(sentenceTokens, tokenDataList);
                }
            }

            TotalItems = tokenDataList.Count;
            AddLog($"Extracted {TotalItems:N0} grammatical sequence tokens with valid case/mood labels.");

            if (tokenDataList.Count == 0)
            {
                throw new InvalidOperationException("No valid training tokens found with case/mood tags.");
            }

            Progress = 30;
            AddLog("Training ML.NET Multiclass classification model (L-BFGS Max Entropy)...");

            var mlContext = new MLContext(seed: 42);
            var trainTestData = mlContext.Data.LoadFromEnumerable(tokenDataList);

            // Split into train (80%) and test (20%)
            var split = mlContext.Data.TrainTestSplit(trainTestData, testFraction: 0.2, seed: 42);

            // ML.NET Pipeline definition
            var pipeline = mlContext.Transforms.Categorical.OneHotHashEncoding("PosMinus2")
                .Append(mlContext.Transforms.Categorical.OneHotHashEncoding("PosMinus1"))
                .Append(mlContext.Transforms.Categorical.OneHotHashEncoding("LemmaMinus1"))
                .Append(mlContext.Transforms.Categorical.OneHotHashEncoding("PosPlus1"))
                .Append(mlContext.Transforms.Categorical.OneHotHashEncoding("PosPlus2"))
                .Append(mlContext.Transforms.Categorical.OneHotHashEncoding("CurrentWord"))
                .Append(mlContext.Transforms.Categorical.OneHotHashEncoding("CurrentPos"))
                .Append(mlContext.Transforms.Concatenate("Features", 
                    "PosMinus2", "PosMinus1", "LemmaMinus1", "PosPlus1", "PosPlus2", "CurrentWord", "CurrentPos"))
                .Append(mlContext.Transforms.Conversion.MapValueToKey("Label"))
                .Append(mlContext.MulticlassClassification.Trainers.LbfgsMaximumEntropy("Label", "Features"))
                .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            Progress = 50;
            var trainedModel = pipeline.Fit(split.TrainSet);

            Progress = 85;
            AddLog("Evaluating model on test set...");
            var predictions = trainedModel.Transform(split.TestSet);
            var metrics = mlContext.MulticlassClassification.Evaluate(predictions);

            ModelAccuracy = metrics.MacroAccuracy;
            AddLog($"Model Macro-Accuracy: {ModelAccuracy * 100:0.00}%");
            AddLog($"Model Micro-Accuracy: {metrics.MicroAccuracy * 100:0.00}%");

            AddLog($"Saving ML.NET model to: {_modelOutputPath}");
            mlContext.Model.Save(trainedModel, split.TrainSet.Schema, _modelOutputPath);

            Status = GrammarPipelineStatus.Completed;
            Progress = 100;
            AddLog("ML.NET model successfully saved.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed model training.");
            Status = GrammarPipelineStatus.Failed;
            ErrorMessage = ex.Message;
            AddLog($"Training failed: {ex.Message}");
        }
    }

    private static void ExtractContextWindows(List<RawToken> tokens, List<GrammarTokenData> result)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            var current = tokens[i];
            
            // Only add to training if it has a target grammatical label
            if (string.IsNullOrEmpty(current.Label)) continue;

            var posMinus2 = i >= 2 ? tokens[i - 2].Pos : "[START]";
            var posMinus1 = i >= 1 ? tokens[i - 1].Pos : "[START]";
            var lemmaMinus1 = i >= 1 ? tokens[i - 1].Lemma : "[START]";
            var posPlus1 = i < tokens.Count - 1 ? tokens[i + 1].Pos : "[END]";
            var posPlus2 = i < tokens.Count - 2 ? tokens[i + 2].Pos : "[END]";

            result.Add(new GrammarTokenData
            {
                PosMinus2 = posMinus2,
                PosMinus1 = posMinus1,
                LemmaMinus1 = lemmaMinus1,
                PosPlus1 = posPlus1,
                PosPlus2 = posPlus2,
                CurrentWord = current.Word,
                CurrentPos = current.Pos,
                Label = current.Label
            });
        }
    }

    private class RawToken
    {
        public string Word { get; set; } = string.Empty;
        public string Pos { get; set; } = string.Empty;
        public string Lemma { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    public class GrammarTokenData
    {
        public string PosMinus2 { get; set; } = string.Empty;
        public string PosMinus1 { get; set; } = string.Empty;
        public string LemmaMinus1 { get; set; } = string.Empty;
        public string PosPlus1 { get; set; } = string.Empty;
        public string PosPlus2 { get; set; } = string.Empty;
        public string CurrentWord { get; set; } = string.Empty;
        public string CurrentPos { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}
