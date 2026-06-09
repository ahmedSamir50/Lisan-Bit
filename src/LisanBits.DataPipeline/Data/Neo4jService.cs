using Neo4j.Driver;

namespace LisanBits.DataPipeline.Data;

public class Neo4jService : IDisposable, IAsyncDisposable
{
    private readonly IDriver _driver;
    private readonly ILogger<Neo4jService> _logger;

    public Neo4jService(IConfiguration configuration, ILogger<Neo4jService> logger)
    {
        _logger = logger;
        
        var section = configuration.GetSection("Neo4j");
        var uri = configuration.GetConnectionString("neo4j") ?? section["BoltUri"] ?? "bolt://localhost:7687";
        var user = section["Username"] ?? "neo4j";
        var pass = section["Password"] ?? "password";

        _logger.LogInformation("Initializing Neo4j Driver to {Uri}", uri);
        _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, pass));
    }

    public async Task EnsureDatabaseConnectedAsync()
    {
        try
        {
            await using var session = _driver.AsyncSession();
            await session.ExecuteReadAsync(async tx =>
            {
                var result = await tx.RunAsync("RETURN 1 AS Test");
                await result.FetchAsync();
                _logger.LogInformation("Successfully connected to Neo4j database.");
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to local Neo4j database. Make sure local Neo4j is running.");
            throw;
        }
    }

    public async Task InsertRootAndWordAsync(string root, string word, string pos, double weight, string context)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(word))
            return;

        await using var session = _driver.AsyncSession();
        try
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                var query = @"
                    MERGE (r:Root {text: $root})
                    MERGE (w:Word {text: $word})
                    MERGE (w)-[rel:DERIVED_FROM]->(r)
                    ON CREATE SET rel.pos = $pos

                    MERGE (c:Context {name: $context})
                    MERGE (w)-[relCtx:BELONGS_TO]->(c)
                    ON CREATE SET relCtx.weight = $weight
                    ON MATCH SET relCtx.weight = CASE WHEN $weight > relCtx.weight THEN $weight ELSE relCtx.weight END
                ";

                await tx.RunAsync(query, new { root, word, pos, weight, context });
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert Root {Root} and Word {Word} into Neo4j", root, word);
        }
    }

    public void Dispose()
    {
        _driver?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_driver != null)
        {
            await _driver.DisposeAsync();
        }
    }
}
