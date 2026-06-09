using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Data.Sqlite;

if (args.Length > 0 &&
    (string.Equals(args[0], "audit", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(args[0], "activate-source", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(args[0], "duplicates", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(args[0], "deduplicate", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(args[0], "reset-queues", StringComparison.OrdinalIgnoreCase)))
{
    var isActivate = string.Equals(args[0], "activate-source", StringComparison.OrdinalIgnoreCase);
    var dbPath = isActivate
        ? (args.Length > 2 ? args[2] : @"D:\A_S\pipeline.db")
        : (args.Length > 1 ? args[1] : @"D:\A_S\pipeline.db");
    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Database file not found: {dbPath}");
        return 1;
    }

    await using var connection = new SqliteConnection($"Data Source={dbPath}");
    await connection.OpenAsync();

    if (string.Equals(args[0], "reset-queues", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("=== Resetting Queues for Active Zero-Yield Sources ===");
        
        var resetQuery = @"
UPDATE CrawledUrlQueue
SET Status = 'Pending', ProcessedAt = NULL
WHERE Status = 'Completed'
  AND DataSourceId IN (
      SELECT d.Id
      FROM DataSourceConfigs d
      LEFT JOIN RawUniversalData r ON r.Source = d.Name
      LEFT JOIN LexiconEntries l ON l.SourceBook = d.Name
      WHERE d.IsActive = 1
      GROUP BY d.Id, d.Name
      HAVING COALESCE(COUNT(r.Id), 0) = 0 AND COALESCE(COUNT(l.Id), 0) = 0
  );";

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = resetQuery;
            var affected = await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"Reset completed queue items back to 'Pending' for zero-yield active sources. Rows affected: {affected}");
        }

        return 0;
    }

    if (string.Equals(args[0], "duplicates", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("=== Checking for duplicates in RawUniversalData ===");
        
        // 1. Total row count vs distinct count
        await PrintQueryAsync(connection,
            "Total vs Distinct Rows",
            @"
SELECT COUNT(*) AS TotalRows,
       COUNT(DISTINCT TextContent) AS UniqueTexts,
       COUNT(*) - COUNT(DISTINCT TextContent) AS DuplicateCount
FROM RawUniversalData;");

        // 2. Duplicates by source
        await PrintQueryAsync(connection,
            "Duplicate Counts by Source",
            @"
SELECT Source, COUNT(*) AS TotalRows, 
       COUNT(*) - COUNT(DISTINCT TextContent) AS DuplicateRows
FROM RawUniversalData
GROUP BY Source
HAVING DuplicateRows > 0
ORDER BY DuplicateRows DESC;");

        // 3. Top 5 duplicate contents
        await PrintQueryAsync(connection,
            "Top 5 Most Frequent Duplicated TextContents",
            @"
SELECT substr(TextContent, 1, 100) AS TextSnippet, COUNT(*) AS Occurrences
FROM RawUniversalData
GROUP BY TextContent
HAVING Occurrences > 1
ORDER BY Occurrences DESC
LIMIT 5;");

        return 0;
    }

    if (string.Equals(args[0], "deduplicate", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("=== Deduplicating RawUniversalData ===");
        
        long beforeCount;
        using (var countCmd = connection.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM RawUniversalData;";
            beforeCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
        }

        Console.WriteLine($"Total rows before deduplication: {beforeCount:N0}");

        int deleted;
        using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.CommandText = @"
DELETE FROM RawUniversalData
WHERE Id NOT IN (
    SELECT MIN(Id)
    FROM RawUniversalData
    GROUP BY TextContent
);";
            deleteCmd.CommandTimeout = 300; // 5 minutes
            
            Console.WriteLine("Running deduplication query (this may take 1-2 minutes)...");
            deleted = await deleteCmd.ExecuteNonQueryAsync();
        }

        long afterCount;
        using (var countCmd = connection.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM RawUniversalData;";
            afterCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
        }

        Console.WriteLine($"Deleted {deleted:N0} duplicate rows.");
        Console.WriteLine($"Total rows after deduplication: {afterCount:N0}");

        return 0;
    }


    if (isActivate)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var sourceId))
        {
            Console.Error.WriteLine("Usage: activate-source <SourceId> [DbPath]");
            return 1;
        }

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "UPDATE DataSourceConfigs SET IsActive = 1 WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", sourceId);
            var affected = await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"Activated source Id {sourceId}. Rows affected: {affected}");
        }

        await PrintQueryAsync(connection,
            "Activated source",
            $@"
SELECT Id, Name, Category, IsActive, BaseUrl, TargetXPath
FROM DataSourceConfigs
WHERE Id = {sourceId};");

        return 0;
    }

    await PrintQueryAsync(connection,
        "Source coverage",
        @"
SELECT Source, Category, COUNT(*) AS Rows, COALESCE(SUM(WordCount), 0) AS Words,
       COALESCE(SUM(SentenceCount), 0) AS Sentences, COALESCE(MAX(ScrapedAt), '') AS LastScraped
FROM RawUniversalData
GROUP BY Source, Category
ORDER BY Words ASC, Rows ASC;");

    await PrintQueryAsync(connection,
        "Lexicon coverage",
        @"
SELECT SourceBook, COUNT(*) AS Entries, MIN(Word) AS MinWord, MAX(Word) AS MaxWord
FROM LexiconEntries
GROUP BY SourceBook;");

    await PrintQueryAsync(connection,
        "Zero-yield or missing sources",
        @"
SELECT d.Id, d.Name, d.Category, d.IsActive,
       COALESCE(COUNT(r.Id), 0) AS Rows,
       COALESCE(SUM(r.WordCount), 0) AS Words,
       COALESCE(MAX(r.ScrapedAt), '') AS LastScraped
FROM DataSourceConfigs d
LEFT JOIN RawUniversalData r ON r.Source = d.Name
GROUP BY d.Id, d.Name, d.Category, d.IsActive
HAVING COALESCE(SUM(r.WordCount), 0) = 0
ORDER BY d.Id;");

    await PrintQueryAsync(connection,
        "Queue states",
        @"
SELECT d.Id, d.Name, d.Category, d.IsActive,
       SUM(CASE WHEN q.Status = 'Pending' THEN 1 ELSE 0 END) AS PendingCount,
       SUM(CASE WHEN q.Status = 'Processing' THEN 1 ELSE 0 END) AS ProcessingCount,
       SUM(CASE WHEN q.Status = 'Completed' THEN 1 ELSE 0 END) AS CompletedCount,
       COALESCE(MAX(q.ProcessedAt), '') AS LastProcessed
FROM DataSourceConfigs d
LEFT JOIN CrawledUrlQueue q ON q.DataSourceId = d.Id
GROUP BY d.Id, d.Name, d.Category, d.IsActive
ORDER BY d.Id;");

    await PrintQueryAsync(connection,
        "Slang totals by source",
        @"
SELECT Source, COUNT(*) AS Rows, COALESCE(SUM(WordCount), 0) AS Words,
       COALESCE(SUM(SentenceCount), 0) AS Sentences, COALESCE(MAX(ScrapedAt), '') AS LastScraped
FROM RawUniversalData
WHERE Category = 'Slang'
GROUP BY Source
ORDER BY LastScraped DESC;");

    await PrintQueryAsync(connection,
        "Recent slang rows",
        @"
SELECT Source, WordCount, SentenceCount, ScrapedAt, substr(TextContent, 1, 120) AS Preview
FROM RawUniversalData
WHERE Category = 'Slang'
ORDER BY ScrapedAt DESC
LIMIT 12;");

    return 0;
}

using var handler = new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli
};
using var client = new HttpClient(handler);
client.Timeout = TimeSpan.FromSeconds(15);
client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

try
{
    var url = "https://www.aleqt.com/%D8%A7%D9%84%D8%A3%D8%AE%D8%A8%D8%A7%D8%B1/%D8%A7%D9%84%D8%AD%D8%B1%D8%A8-%D8%AA%D9%81%D9%82%D8%AF-%D8%A5%D9%8A%D8%B1%D8%A7%D9%86-7-%D9%85%D9%86-%D9%82%D8%AF%D8%B1%D8%AA%D9%87%D8%A7-%D8%B9%D9%84%D9%89-%D8%AA%D9%88%D9%84%D9%8A%D8%AF-%D8%A7%D9%84%D9%83%D9%87%D8%B1%D8%A8%D8%A7%D8%A1-10495";
    var html = await client.GetStringAsync(url);
    var doc = new HtmlDocument();
    doc.LoadHtml(html);

    Console.WriteLine("=== Original TargetXPath Matched Text ===");
    var origXPath = "//div[contains(@class, 'article-content')]/div[not(@class) or @class='']";
    var origNodes = doc.DocumentNode.SelectNodes(origXPath);
    if (origNodes != null)
    {
        foreach (var n in origNodes)
        {
            Console.WriteLine($"- Node: {n.InnerText.Trim()}");
        }
    }

    Console.WriteLine("\n=== Fallback '//div[contains(@class,\"content\")]//p' Matched Text ===");
    var fallbackXPath = "//div[contains(@class,'content')]//p";
    var fallbackNodes = doc.DocumentNode.SelectNodes(fallbackXPath);
    if (fallbackNodes != null)
    {
        foreach (var n in fallbackNodes)
        {
            Console.WriteLine($"- Para: {n.InnerText.Trim()}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

return 0;

static async Task PrintQueryAsync(SqliteConnection connection, string title, string sql)
{
    Console.WriteLine($"=== {title} ===");

    await using var command = connection.CreateCommand();
    command.CommandText = sql;

    await using var reader = await command.ExecuteReaderAsync();
    var columns = Enumerable.Range(0, reader.FieldCount)
        .Select(reader.GetName)
        .ToArray();

    Console.WriteLine(string.Join(" | ", columns));

    while (await reader.ReadAsync())
    {
        var values = new string[reader.FieldCount];
        for (var index = 0; index < reader.FieldCount; index++)
        {
            values[index] = reader.IsDBNull(index) ? string.Empty : Convert.ToString(reader.GetValue(index)) ?? string.Empty;
        }

        Console.WriteLine(string.Join(" | ", values));
    }

    Console.WriteLine();
}


