using LisanBits.GrammarPipeline;
 using LisanBits.DataPipeline.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Neo4j.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Neo4j
var connectionString = builder.Configuration.GetConnectionString("neo4j") ?? "bolt://localhost:7687";
var user = builder.Configuration["Neo4j:Username"] ?? "neo4j";
var pass = builder.Configuration["Neo4j:Password"] ?? "password";

builder.Services.AddSingleton<IDriver>(sp => GraphDatabase.Driver(connectionString, AuthTokens.Basic(user, pass)));
builder.Services.AddSingleton<GrammarManagerService>();

// PipelineDbContext (shared with DataPipeline — read-only for training data)
builder.Services.AddDbContext<PipelineDbContext>(opts =>
    opts.UseSqlite(builder.Configuration.GetConnectionString("PipelineDb"))
        .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

// Context Classifier (Genus-Aware, two-level cascade)
builder.Services.Configure<ContextClassifierOptions>(
    builder.Configuration.GetSection(ContextClassifierOptions.SectionName));
builder.Services.AddSingleton<ContextClassifierManager>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/api/grammar/status", (GrammarManagerService manager) =>
{
    return Results.Ok(new
    {
        status = manager.Status.ToString(),
        progress = manager.Progress,
        processedItems = manager.ProcessedItems,
        totalItems = manager.TotalItems,
        mergedNodes = manager.MergedNodes,
        mergedEdges = manager.MergedEdges,
        modelAccuracy = manager.ModelAccuracy,
        errorMessage = manager.ErrorMessage,
        logs = manager.Logs.ToArray()
    });
});

app.MapPost("/api/grammar/ingest", (GrammarManagerService manager) =>
{
    var started = manager.StartIngest();
    return started ? Results.Accepted("/api/grammar/status") : Results.Conflict("An operation is already in progress.");
});

app.MapPost("/api/grammar/mine", (GrammarManagerService manager) =>
{
    var started = manager.StartMine();
    return started ? Results.Accepted("/api/grammar/status") : Results.Conflict("An operation is already in progress.");
});

app.MapPost("/api/grammar/train", (GrammarManagerService manager) =>
{
    var started = manager.StartTrain();
    return started ? Results.Accepted("/api/grammar/status") : Results.Conflict("An operation is already in progress.");
});

// ---- Context Classifier Endpoints ----
app.MapPost("/api/context/train", (ContextClassifierManager classifier) =>
{
    var started = classifier.StartTraining();
    return started
        ? Results.Accepted("/api/context/status", new { message = "Context classifier training started." })
        : Results.Conflict("Context classifier training is already in progress.");
});

app.MapGet("/api/context/status", (ContextClassifierManager classifier) =>
    Results.Ok(new
    {
        status = classifier.Status.ToString(),
        progress = classifier.Progress,
        currentOperation = classifier.CurrentOperation,
        errorMessage = classifier.ErrorMessage,
        logs = classifier.Logs.ToArray()
    }));

app.MapPost("/api/grammar/reset", async (GrammarManagerService manager) =>
{
    try
    {
        await manager.ResetGrammarAsync();
        return Results.Ok("Grammar data reset successfully.");
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
