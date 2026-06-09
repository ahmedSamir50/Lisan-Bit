using Neo4j.Driver;
using LisanBits.WebApi.Models;

namespace LisanBits.WebApi.Services;

public class GraphCacheService
{
    private readonly IDriver _driver;
    private readonly ILogger<GraphCacheService> _logger;

    private readonly Dictionary<string, int> _wordToId = new(StringComparer.OrdinalIgnoreCase);
    private string[] _idToWord = Array.Empty<string>();
    
    private readonly Dictionary<string, int> _rootToId = new(StringComparer.OrdinalIgnoreCase);
    private string[] _idToRoot = Array.Empty<string>();

    private readonly Dictionary<int, int[]> _rootToWordsAdjacency = new();
    private ContextFlags[] _wordContexts = Array.Empty<ContextFlags>();

    private bool _isLoaded = false;
    private int _relationshipCount = 0;

    public GraphCacheService(IDriver driver, ILogger<GraphCacheService> logger)
    {
        _driver = driver;
        _logger = logger;
    }

    public bool IsLoaded => _isLoaded;
    public int WordCount => _idToWord.Length;
    public int RootCount => _idToRoot.Length;
    public int RelationshipCount => _relationshipCount;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing high-performance Graph Cache...");

        try
        {
            // 1. Fetch all words
            var words = await FetchAllWordsAsync();
            _logger.LogInformation("Loaded {WordCount} words.", words.Count);

            // 2. Fetch all roots
            var roots = await FetchAllRootsAsync();
            _logger.LogInformation("Loaded {RootCount} roots.", roots.Count);

            // Build bidirectional vocabulary dictionaries
            var wordList = new string[words.Count];
            var wordToIdLocal = new Dictionary<string, int>(words.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < words.Count; i++)
            {
                wordList[i] = words[i];
                wordToIdLocal[words[i]] = i;
            }

            var rootList = new string[roots.Count];
            var rootToIdLocal = new Dictionary<string, int>(roots.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < roots.Count; i++)
            {
                rootList[i] = roots[i];
                rootToIdLocal[roots[i]] = i;
            }

            // 3. Fetch derivations and build adjacency list
            var derivations = await FetchDerivationsAsync();
            var adjacencyBuilder = new Dictionary<int, List<int>>();
            int validDerivationCount = 0;

            foreach (var (wordText, rootText) in derivations)
            {
                if (wordToIdLocal.TryGetValue(wordText, out int wordId) &&
                    rootToIdLocal.TryGetValue(rootText, out int rootId))
                {
                    if (!adjacencyBuilder.TryGetValue(rootId, out var wordListForRoot))
                    {
                        wordListForRoot = new List<int>();
                        adjacencyBuilder[rootId] = wordListForRoot;
                    }
                    wordListForRoot.Add(wordId);
                    validDerivationCount++;
                }
            }

            var rootToWordsAdjacencyLocal = new Dictionary<int, int[]>(adjacencyBuilder.Count);
            foreach (var (rootId, list) in adjacencyBuilder)
            {
                rootToWordsAdjacencyLocal[rootId] = list.ToArray();
            }

            // 4. Fetch context assignments and build flags
            var contexts = await FetchContextsAsync();
            var wordContextsLocal = new ContextFlags[words.Count];
            int contextRelCount = 0;

            foreach (var (wordText, contextName) in contexts)
            {
                if (wordToIdLocal.TryGetValue(wordText, out int wordId))
                {
                    var flag = ParseContextFlag(contextName);
                    wordContextsLocal[wordId] |= flag;
                    contextRelCount++;
                }
            }

            // Apply to read-only/non-readonly fields
            lock (_wordToId)
            {
                _wordToId.Clear();
                foreach (var kvp in wordToIdLocal) _wordToId.Add(kvp.Key, kvp.Value);
            }
            _idToWord = wordList;

            lock (_rootToId)
            {
                _rootToId.Clear();
                foreach (var kvp in rootToIdLocal) _rootToId.Add(kvp.Key, kvp.Value);
            }
            _idToRoot = rootList;

            lock (_rootToWordsAdjacency)
            {
                _rootToWordsAdjacency.Clear();
                foreach (var kvp in rootToWordsAdjacencyLocal) _rootToWordsAdjacency.Add(kvp.Key, kvp.Value);
            }
            _wordContexts = wordContextsLocal;

            _relationshipCount = validDerivationCount + contextRelCount;
            _isLoaded = true;

            _logger.LogInformation("Graph Cache successfully loaded. Words: {Words}, Roots: {Roots}, Seeded Relationships: {Rels}", 
                WordCount, RootCount, _relationshipCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Graph Cache from Neo4j.");
            throw;
        }
    }

    // Lookup operations
    public int GetWordId(string text)
    {
        lock (_wordToId)
        {
            return _wordToId.TryGetValue(text, out int id) ? id : -1;
        }
    }

    public string? GetWordText(int id)
    {
        if (id >= 0 && id < _idToWord.Length)
        {
            return _idToWord[id];
        }
        return null;
    }

    public int GetRootId(string text)
    {
        lock (_rootToId)
        {
            return _rootToId.TryGetValue(text, out int id) ? id : -1;
        }
    }

    public string? GetRootText(int id)
    {
        if (id >= 0 && id < _idToRoot.Length)
        {
            return _idToRoot[id];
        }
        return null;
    }

    public int[] GetDerivations(int rootId)
    {
        lock (_rootToWordsAdjacency)
        {
            return _rootToWordsAdjacency.TryGetValue(rootId, out var derivations) ? derivations : Array.Empty<int>();
        }
    }

    public ContextFlags GetContext(int wordId)
    {
        if (wordId >= 0 && wordId < _wordContexts.Length)
        {
            return _wordContexts[wordId];
        }
        return ContextFlags.None;
    }

    public bool MatchesContext(int wordId, ContextFlags activeContext)
    {
        if (activeContext == ContextFlags.None) return true;
        return (GetContext(wordId) & activeContext) != ContextFlags.None;
    }

    // Database querying helpers
    private async Task<List<string>> FetchAllWordsAsync()
    {
        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var result = await tx.RunAsync("MATCH (w:Word) RETURN w.text AS text");
            var list = new List<string>();
            while (await result.FetchAsync())
            {
                var text = result.Current["text"].As<string>();
                if (!string.IsNullOrEmpty(text)) list.Add(text);
            }
            return list;
        });
    }

    private async Task<List<string>> FetchAllRootsAsync()
    {
        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var result = await tx.RunAsync("MATCH (r:Root) RETURN r.text AS text");
            var list = new List<string>();
            while (await result.FetchAsync())
            {
                var text = result.Current["text"].As<string>();
                if (!string.IsNullOrEmpty(text)) list.Add(text);
            }
            return list;
        });
    }

    private async Task<List<(string Word, string Root)>> FetchDerivationsAsync()
    {
        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var result = await tx.RunAsync("MATCH (w:Word)-[:DERIVED_FROM]->(r:Root) RETURN w.text AS word, r.text AS root");
            var list = new List<(string Word, string Root)>();
            while (await result.FetchAsync())
            {
                var word = result.Current["word"].As<string>();
                var root = result.Current["root"].As<string>();
                if (!string.IsNullOrEmpty(word) && !string.IsNullOrEmpty(root))
                {
                    list.Add((word, root));
                }
            }
            return list;
        });
    }

    private async Task<List<(string Word, string Context)>> FetchContextsAsync()
    {
        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var result = await tx.RunAsync("MATCH (w:Word)-[:BELONGS_TO]->(c:Context) RETURN w.text AS word, c.name AS context");
            var list = new List<(string Word, string Context)>();
            while (await result.FetchAsync())
            {
                var word = result.Current["word"].As<string>();
                var context = result.Current["context"].As<string>();
                if (!string.IsNullOrEmpty(word) && !string.IsNullOrEmpty(context))
                {
                    list.Add((word, context));
                }
            }
            return list;
        });
    }

    private static ContextFlags ParseContextFlag(string contextName)
    {
        if (Enum.TryParse<ContextFlags>(contextName, true, out var flag))
        {
            return flag;
        }
        return ContextFlags.General;
    }
}
