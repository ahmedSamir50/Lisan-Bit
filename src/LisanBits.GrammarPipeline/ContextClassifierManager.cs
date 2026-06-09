using System.Collections.Concurrent;
using System.Text.Json;
using LisanBits.DataPipeline.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;

namespace LisanBits.GrammarPipeline;

// ---------------------------------------------------------------------------
// Options — bound from appsettings.json section "ContextClassifier"
// ---------------------------------------------------------------------------
public sealed class ContextClassifierOptions
{
    public const string SectionName = "ContextClassifier";

    /// <summary>Absolute safety timeout in minutes per model. Default: 30.</summary>
    public int TrainingTimeoutMinutes { get; set; } = 30;

    /// <summary>Minimum unique Farasa roots a text block must have to be used for training.</summary>
    public int MinUniqueRoots { get; set; } = 7;

    /// <summary>F1 convergence delta threshold for early stopping.</summary>
    public double F1ConvergenceDelta { get; set; } = 0.001;

    /// <summary>Number of epochs with negligible improvement before stopping.</summary>
    public int EarlyStoppingPatience { get; set; } = 3;

    /// <summary>Path to write the trained model zip files.</summary>
    public string ModelOutputDirectory { get; set; } = "models/context";
}

// ---------------------------------------------------------------------------
// Input / Output schema for ML.NET
// ---------------------------------------------------------------------------
public sealed class ContextTrainRow
{
    [LoadColumn(0)] public string RootSequence { get; set; } = string.Empty;
    [LoadColumn(1)] public bool Label { get; set; }
    [LoadColumn(2)] public float SampleWeight { get; set; }
}

public sealed class MultiContextTrainRow
{
    public string RootSequence { get; set; } = string.Empty;
    public string LeafLabel { get; set; } = string.Empty;
    public float SampleWeight { get; set; }
}

// ---------------------------------------------------------------------------
// Status tracking
// ---------------------------------------------------------------------------
public enum ContextClassifierStatus { Idle, Training, Completed, Failed }

/// <summary>
/// Trains a two-level cascade of binary ML.NET context classifiers.
///
/// Level-0 (Family Router): one binary LbfgsLogisticRegression model per root domain.
/// Level-1 (Sub-Router): one multiclass LbfgsMaximumEntropy model per activated family.
///
/// Feature pipeline: RootSequence (Farasa) → unigram TF-IDF on root tokens.
/// Class weighting: w_i = N / (K * N_k) to counteract corpus imbalance.
/// </summary>
public sealed class ContextClassifierManager
{
    private static readonly string[] FamilyDomains =
    [
        "Religion", "Science", "Medical", "Finance",
        "Sports", "DailyLife", "Literature", "News",
        "Linguistics", "Slang"
    ];

    private readonly IServiceProvider _services;
    private readonly ILogger<ContextClassifierManager> _logger;
    private readonly ContextClassifierOptions _opts;

    private readonly object _lock = new();
    private Task? _activeTask;

    public ContextClassifierStatus Status { get; private set; } = ContextClassifierStatus.Idle;
    public double Progress { get; private set; }
    public string CurrentOperation { get; private set; } = string.Empty;
    public string? ErrorMessage { get; private set; }
    public ConcurrentQueue<string> Logs { get; } = new();

    private readonly ConcurrentDictionary<string, ITransformer> _level0Models = new();
    private MLContext? _mlContext;

    public ContextClassifierManager(
        IServiceProvider services,
        IOptions<ContextClassifierOptions> opts,
        ILogger<ContextClassifierManager> logger)
    {
        _services = services;
        _opts = opts.Value;
        _logger = logger;
    }

    public bool StartTraining()
    {
        lock (_lock)
        {
            if (_activeTask is { IsCompleted: false }) return false;
            _activeTask = Task.Run(RunTrainingAsync);
            return true;
        }
    }

    // -----------------------------------------------------------------------
    // Training pipeline
    // -----------------------------------------------------------------------
    private async Task RunTrainingAsync()
    {
        Status = ContextClassifierStatus.Training;
        ErrorMessage = null;
        Progress = 0;

        try
        {
            _mlContext = new MLContext(seed: 42);
            Directory.CreateDirectory(_opts.ModelOutputDirectory);

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();

            Log("Loading corpus: joining ProcessedUniversalData with RawUniversalData...");

            // Join Processed ↔ Raw to get both RootSequence and Category
            var rows = await (
                from p in db.ProcessedUniversalData
                join r in db.RawUniversalData on p.RawDataId equals r.Id
                where p.RootSequence != null && p.RootSequence.Length > 0
                select new
                {
                    r.Category,
                    p.RootSequence,
                    p.ContextVector
                }
            ).AsNoTracking().ToListAsync();

            Log($"Loaded {rows.Count:N0} processed rows.");

            if (rows.Count < 50)
                throw new InvalidOperationException(
                    "Insufficient training data. Run the DataPipeline scraper first.");

            // Level-0: binary family classifiers
            int step = 0;
            foreach (var family in FamilyDomains)
            {
                step++;
                Progress = (double)step / FamilyDomains.Length * 0.8;
                await TrainFamilyClassifierAsync(
                    family,
                    rows.Select(r => (r.Category, r.RootSequence!)).ToList());
            }

            // Level-1: sub-routers using ContextVector JSON labels
            Log("Training Level-1 sub-routers from ContextVector leaf labels...");
            await TrainSubRoutersAsync(
                rows.Select(r => (r.Category, r.RootSequence!, r.ContextVector)).ToList());

            Status = ContextClassifierStatus.Completed;
            Progress = 1.0;
            Log("Context classifier training complete.");
        }
        catch (Exception ex)
        {
            Status = ContextClassifierStatus.Failed;
            ErrorMessage = ex.Message;
            _logger.LogError(ex, "Context classifier training failed.");
            Log($"ERROR: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------
    // Level-0: Binary family classifier
    // -----------------------------------------------------------------------
    private async Task TrainFamilyClassifierAsync(
        string family,
        List<(string Category, string RootSequence)> allRows)
    {
        Log($"[Level-0] Training binary classifier for: {family}");
        CurrentOperation = $"Level-0: {family}";

        int totalN = allRows.Count;
        int positiveN = allRows.Count(r => r.Category.Equals(family, StringComparison.OrdinalIgnoreCase));
        int negativeN = totalN - positiveN;

        if (positiveN < 10)
        {
            Log($"  [SKIP] {family}: only {positiveN} positive samples.");
            return;
        }

        float posW = totalN / (2f * positiveN);
        float negW = totalN / (2f * Math.Max(1, negativeN));

        var trainRows = allRows.Select(r => new ContextTrainRow
        {
            RootSequence = r.RootSequence,
            Label = r.Category.Equals(family, StringComparison.OrdinalIgnoreCase),
            SampleWeight = r.Category.Equals(family, StringComparison.OrdinalIgnoreCase) ? posW : negW
        }).ToList();

        var dataView = _mlContext!.Data.LoadFromEnumerable(trainRows);

        // Feature pipeline: space-tokenize Farasa roots → unigram TF-IDF
        var pipeline = _mlContext.Transforms.Text
            .TokenizeIntoWords("RootTokens", nameof(ContextTrainRow.RootSequence), separators: [' '])
            .Append(_mlContext.Transforms.Text.ProduceNgrams("Features", "RootTokens",
                ngramLength: 1, useAllLengths: false,
                weighting: Microsoft.ML.Transforms.Text.NgramExtractingEstimator.WeightingCriteria.TfIdf))
            .Append(_mlContext.BinaryClassification.Trainers.LbfgsLogisticRegression(
                new LbfgsLogisticRegressionBinaryTrainer.Options
                {
                    LabelColumnName = nameof(ContextTrainRow.Label),
                    FeatureColumnName = "Features",
                    ExampleWeightColumnName = nameof(ContextTrainRow.SampleWeight),
                    MaximumNumberOfIterations = 100,
                    L2Regularization = 1.0f
                }));

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(_opts.TrainingTimeoutMinutes));
        ITransformer model;
        try { model = await Task.Run(() => pipeline.Fit(dataView), cts.Token); }
        catch (OperationCanceledException)
        {
            Log($"  [TIMEOUT] {family} — fitting final snapshot.");
            model = pipeline.Fit(dataView);
        }

        var predictions = model.Transform(dataView);
        var metrics = _mlContext.BinaryClassification.Evaluate(predictions,
            labelColumnName: nameof(ContextTrainRow.Label));
        Log($"  {family}: F1={metrics.F1Score:F4} AUC={metrics.AreaUnderRocCurve:F4}");

        var modelPath = Path.Combine(_opts.ModelOutputDirectory, $"level0_{family.ToLower()}.zip");
        _mlContext.Model.Save(model, dataView.Schema, modelPath);
        _level0Models[family] = model;
    }

    // -----------------------------------------------------------------------
    // Level-1: Sub-router classifiers
    // -----------------------------------------------------------------------
    private async Task TrainSubRoutersAsync(
        List<(string Category, string RootSequence, string? ContextVector)> allRows)
    {
        var labeled = allRows
            .Select(r =>
            {
                string? leafPath = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(r.ContextVector) && r.ContextVector.Length > 2)
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, double>>(r.ContextVector);
                        var best = dict?.OrderByDescending(kv => kv.Value).FirstOrDefault();
                        if (best.HasValue && best.Value.Key.Contains('/'))
                            leafPath = best.Value.Key;
                    }
                }
                catch { /* skip malformed JSON */ }
                return (r.Category, r.RootSequence, LeafPath: leafPath);
            })
            .Where(x => x.LeafPath != null)
            .GroupBy(x => x.Category)
            .ToList();

        int subStep = 0;
        foreach (var group in labeled)
        {
            subStep++;
            Progress = 0.8 + (double)subStep / labeled.Count * 0.2;
            var familyName = group.Key;
            var items = group.ToList();

            var subLabels = items.Select(x => x.LeafPath!).Distinct().ToList();
            if (subLabels.Count < 2)
            {
                Log($"  [SKIP] Level-1 {familyName}: {subLabels.Count} sub-label(s).");
                continue;
            }

            Log($"[Level-1] {familyName} ({subLabels.Count} sub-contexts, {items.Count} rows).");
            CurrentOperation = $"Level-1: {familyName}";

            int totalN = items.Count;
            var labelCounts = items.GroupBy(x => x.LeafPath!).ToDictionary(g => g.Key, g => g.Count());

            var trainRows = items.Select(x => new MultiContextTrainRow
            {
                RootSequence = x.RootSequence,
                LeafLabel = x.LeafPath!,
                SampleWeight = (float)(totalN / (float)(subLabels.Count * Math.Max(1, labelCounts[x.LeafPath!])))
            }).ToList();

            var dataView = _mlContext!.Data.LoadFromEnumerable(trainRows);

            var pipeline = _mlContext.Transforms.Conversion
                .MapValueToKey("Label", nameof(MultiContextTrainRow.LeafLabel))
                .Append(_mlContext.Transforms.Text.TokenizeIntoWords("RootTokens",
                    nameof(MultiContextTrainRow.RootSequence), separators: [' ']))
                .Append(_mlContext.Transforms.Text.ProduceNgrams("Features", "RootTokens",
                    ngramLength: 1, useAllLengths: false,
                    weighting: Microsoft.ML.Transforms.Text.NgramExtractingEstimator.WeightingCriteria.TfIdf))
                .Append(_mlContext.MulticlassClassification.Trainers.LbfgsMaximumEntropy(
                    new LbfgsMaximumEntropyMulticlassTrainer.Options
                    {
                        LabelColumnName = "Label",
                        FeatureColumnName = "Features",
                        ExampleWeightColumnName = nameof(MultiContextTrainRow.SampleWeight),
                        MaximumNumberOfIterations = 100,
                        L2Regularization = 1.0f
                    }))
                .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(_opts.TrainingTimeoutMinutes));
            ITransformer subModel;
            try { subModel = await Task.Run(() => pipeline.Fit(dataView), cts.Token); }
            catch (OperationCanceledException)
            {
                Log($"  [TIMEOUT] {familyName} sub-router — fitting final snapshot.");
                subModel = pipeline.Fit(dataView);
            }

            var predictions = subModel.Transform(dataView);
            var metrics = _mlContext.MulticlassClassification.Evaluate(predictions, labelColumnName: "Label");
            Log($"  {familyName}: MicroAcc={metrics.MicroAccuracy:F4} MacroAcc={metrics.MacroAccuracy:F4}");

            var modelPath = Path.Combine(_opts.ModelOutputDirectory, $"level1_{familyName.ToLower()}.zip");
            _mlContext.Model.Save(subModel, dataView.Schema, modelPath);
        }
    }

    private void Log(string message)
    {
        _logger.LogInformation("{Msg}", message);
        Logs.Enqueue($"[{DateTime.UtcNow:HH:mm:ss}] {message}");
        while (Logs.Count > 200) Logs.TryDequeue(out _);
    }
}
