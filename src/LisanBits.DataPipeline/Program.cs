using LisanBits.DataPipeline;
using LisanBits.DataPipeline.Data;
using LisanBits.DataPipeline.Acquisition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// Add SQLite DbContext for Checkpointing and State Management
builder.Services.AddDbContext<PipelineDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("PipelineDb"))
           .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
});

// Register Neo4j Database Service
builder.Services.AddSingleton<Neo4jService>();

// Register Preprocessing DataCleaner
builder.Services.AddSingleton<LisanBits.DataPipeline.Preprocessing.DataCleaner>();

// Register Universal Scraper with Polly standard resilience
builder.Services.AddHttpClient<UniversalHtmlScraper>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    .AddStandardResilienceHandler();

// Register Farasa Preprocessing Service
// Aspire service discovery: named endpoints use http://_endpointName.serviceName
// "farasa-endpoint" is the named endpoint on the "farasa-api" container resource.
builder.Services.AddHttpClient<LisanBits.DataPipeline.Preprocessing.FarasaPreprocessingService>(client => 
{
    client.BaseAddress = new Uri("http://_farasa-endpoint.farasa-api");
})
.AddServiceDiscovery()
.AddStandardResilienceHandler();

builder.Services.AddHostedService<Worker>();
// Resolve the hosted service from the typed client registration so HttpClient has BaseAddress/resilience configured.
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<LisanBits.DataPipeline.Preprocessing.FarasaPreprocessingService>());

var host = builder.Build();

if (builder.Environment.IsDevelopment())
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();
    db.Database.Migrate();
}

host.Run();
