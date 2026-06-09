using LisanBits.Dashboard.Components;
using LisanBits.DataPipeline.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient("ConceptNetImporter", client =>
{
    client.BaseAddress = new Uri("http://conceptnet-importer");
})
.AddServiceDiscovery();

builder.Services.AddHttpClient("GrammarPipeline", client =>
{
    client.BaseAddress = new Uri("http://grammar-pipeline");
})
.AddServiceDiscovery();

// Use the same absolute database path as DataPipeline
builder.Services.AddDbContextFactory<PipelineDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("PipelineDb"));
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<LisanBits.Dashboard.Services.ScraperProgressService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Keep SignalR hub responses untouched; status-code re-execute can break hub protocol framing.
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/scraperhub"), branch =>
{
    branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    branch.UseHttpsRedirection();
});

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<LisanBits.Dashboard.Hubs.ScraperHub>("/scraperhub");

app.Run();
