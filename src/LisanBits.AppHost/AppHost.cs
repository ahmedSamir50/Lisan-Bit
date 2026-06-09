var builder = DistributedApplication.CreateBuilder(args);

var farasaApi = builder.AddDockerfile("farasa-api", "../farasa-api")
                       .WithHttpEndpoint(targetPort: 8000, name: "farasa-endpoint")
                       .WithHttpHealthCheck("/health", endpointName: "farasa-endpoint");

var pipelineDb = builder.AddConnectionString("PipelineDb");

EndpointReference farasa_endpointReference = farasaApi.GetEndpoint("farasa-endpoint");

// Read Neo4j password from AppHost configurations
var neo4jPassword = builder.Configuration["Neo4j:Password"] ?? "lisanbits_password";

// Configure Neo4j container resource
var neo4j = builder.AddContainer("neo4j", "neo4j", "5")
                   .WithHttpEndpoint(targetPort: 7474, port: 7474, name: "http")
                   .WithEndpoint(targetPort: 7687, port: 7687, scheme: "bolt", name: "bolt")
                   .WithEnvironment("NEO4J_AUTH", $"neo4j/{neo4jPassword}");

var conceptNetImporter = builder.AddProject<Projects.LisanBits_ConceptNetImporter>("conceptnet-importer")
                               .WithEnvironment("ConnectionStrings__neo4j", neo4j.GetEndpoint("bolt"))
                               .WithEnvironment("Neo4j__Username", "neo4j")
                               .WithEnvironment("Neo4j__Password", neo4jPassword)
                               .WaitFor(neo4j);

var grammarPipeline = builder.AddProject<Projects.LisanBits_GrammarPipeline>("grammar-pipeline")
                             .WithReference(pipelineDb)
                             .WithEnvironment("ConnectionStrings__neo4j", neo4j.GetEndpoint("bolt"))
                             .WithEnvironment("Neo4j__Username", "neo4j")
                             .WithEnvironment("Neo4j__Password", neo4jPassword)
                             .WaitFor(neo4j);

// Inject all configuration settings under "GrammarPipeline" dynamically
void InjectSection(Microsoft.Extensions.Configuration.IConfigurationSection section)
{
    foreach (var child in section.GetChildren())
    {
        if (child.Value != null)
        {
            var envKey = child.Path.Replace(":", "__");
            grammarPipeline.WithEnvironment(envKey, child.Value);
        }
        else
        {
            InjectSection(child);
        }
    }
}
InjectSection(builder.Configuration.GetSection("GrammarPipeline"));

var webApi = builder.AddProject<Projects.LisanBits_WebApi>("web-api")
                    .WithEnvironment("ConnectionStrings__neo4j", neo4j.GetEndpoint("bolt"))
                    .WithEnvironment("Neo4j__Username", "neo4j")
                    .WithEnvironment("Neo4j__Password", neo4jPassword)
                    .WaitFor(neo4j);

var dashboard = builder.AddProject<Projects.LisanBits_Dashboard>("lisanbits-dashboard")
                       .WithReference(pipelineDb)
                       .WithReference(conceptNetImporter)
                       .WithReference(grammarPipeline)
                       .WithReference(webApi);

var dataPipeline = builder.AddProject<Projects.LisanBits_DataPipeline>("lisanbits-datapipeline")
                           .WithReference(farasaApi.GetEndpoint("farasa-endpoint"))
                           .WithReference(pipelineDb)
                           .WithEnvironment("ConnectionStrings__neo4j", neo4j.GetEndpoint("bolt"))
                           .WithEnvironment("Neo4j__Username", "neo4j")
                           .WithEnvironment("Neo4j__Password", neo4jPassword)
                           .WithReference(dashboard)
                           .WaitFor(farasaApi)
                           .WaitFor(neo4j)
                           .WaitFor(dashboard);

builder.Build().Run();
