using Neo4j.Driver;
using LisanBits.WebApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("neo4j") ?? "bolt://localhost:7687";
var user = builder.Configuration["Neo4j:Username"] ?? "neo4j";
var pass = builder.Configuration["Neo4j:Password"] ?? "password";

builder.Services.AddSingleton<IDriver>(sp => GraphDatabase.Driver(connectionString, AuthTokens.Basic(user, pass)));
builder.Services.AddSingleton<GraphCacheService>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Diagnostic Cache Endpoint
app.MapGet("/api/cache/status", (GraphCacheService cache) =>
{
    return Results.Ok(new
    {
        isLoaded = cache.IsLoaded,
        wordCount = cache.WordCount,
        rootCount = cache.RootCount,
        relationshipCount = cache.RelationshipCount,
        message = cache.IsLoaded ? "Graph cache is warm and active." : "Graph cache is currently loading from Neo4j..."
    });
});

// Trigger Cache Initialization in the background during startup
_ = Task.Run(async () =>
{
    try
    {
        using (var scope = app.Services.CreateScope())
        {
            var cache = scope.ServiceProvider.GetRequiredService<GraphCacheService>();
            await cache.InitializeAsync();
        }
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Background Graph Cache initialization failed.");
    }
});

app.Run();
