using LisanBits.GraphSeeder;
using LisanBits.DataPipeline.Data;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configure EF Core SQLite Database
builder.Services.AddDbContext<PipelineDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("PipelineDb")
                      ?? throw new InvalidOperationException("Connection string 'PipelineDb' is not configured."));
});

// Configure Neo4j connection purely from Aspire connection string/environment variables
var connectionString = builder.Configuration.GetConnectionString("neo4j")
                       ?? throw new InvalidOperationException("Neo4j connection string is missing.");
var user = builder.Configuration["Neo4j:Username"]
           ?? throw new InvalidOperationException("Neo4j username environment variable is missing.");
var pass = builder.Configuration["Neo4j:Password"]
           ?? throw new InvalidOperationException("Neo4j password environment variable is missing.");

builder.Services.AddSingleton<IDriver>(sp => 
    GraphDatabase.Driver(connectionString, AuthTokens.Basic(user, pass)));

builder.Services.AddSingleton<GraphSeedingService>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/api/seeder/status", (GraphSeedingService seeder) =>
{
    return Results.Ok(new
    {
        status = seeder.Status.ToString(),
        progress = seeder.Progress,
        processedCount = seeder.ProcessedCount,
        totalEntries = seeder.TotalEntries,
        errorMessage = seeder.ErrorMessage
    });
});

app.MapPost("/api/seeder/import", (GraphSeedingService seeder) =>
{
    var started = seeder.StartSeeding();
    return started ? Results.Accepted("/api/seeder/status") : Results.Conflict("Seeding is already in progress.");
});

app.MapPost("/api/seeder/reset", async (GraphSeedingService seeder) =>
{
    try
    {
        await seeder.ResetSeedingAsync();
        return Results.Ok("Lexicon graph data reset successfully.");
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
