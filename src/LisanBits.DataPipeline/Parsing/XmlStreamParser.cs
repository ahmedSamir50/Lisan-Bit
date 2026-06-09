using System.Runtime.CompilerServices;
using System.Xml;

namespace LisanBits.DataPipeline.Parsing;

/// <summary>
/// High-performance XML parser designed for gigabyte-scale dumps (like Wikipedia and Tanzil).
/// Uses forward-only XmlReader to guarantee O(1) memory complexity.
/// NEVER uses XmlDocument or XDocument.
/// </summary>
public class XmlStreamParser
{
    private readonly ILogger<XmlStreamParser> _logger;

    public XmlStreamParser(ILogger<XmlStreamParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses a Tanzil Quran XML dump. 
    /// Streams Aya elements out as they are parsed to prevent memory build-up.
    /// </summary>
    public async IAsyncEnumerable<AyaItem> ParseTanzilDumpAsync(string filePath, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Parsing Tanzil XML dump: {FilePath}", filePath);

        var settings = new XmlReaderSettings
        {
            Async = true,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };

        // Standard FileStream with appropriate buffering
        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var reader = XmlReader.Create(fileStream, settings);

        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.Element && reader.Name == "aya")
            {
                var index = reader.GetAttribute("index");
                var text = reader.GetAttribute("text");
                var sura = reader.GetAttribute("sura");
                var ayaNum = reader.GetAttribute("aya");

                if (!string.IsNullOrEmpty(text))
                {
                    yield return new AyaItem
                    {
                        Index = index ?? string.Empty,
                        Sura = sura ?? string.Empty,
                        AyaNumber = ayaNum ?? string.Empty,
                        Text = text
                    };
                }
            }
        }
    }

    /// <summary>
    /// Parses an Arabic Wikipedia XML dump.
    /// Streams Page elements out to extract text and category tags.
    /// </summary>
    public async IAsyncEnumerable<WikiPageItem> ParseWikipediaDumpAsync(string filePath, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Parsing Wikipedia XML dump: {FilePath}", filePath);

        var settings = new XmlReaderSettings
        {
            Async = true,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };

        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var reader = XmlReader.Create(fileStream, settings);

        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.Element && reader.Name == "page")
            {
                // We use ReadSubtree to parse the page element without loading the entire document
                using var pageReader = reader.ReadSubtree();
                
                string title = string.Empty;
                string text = string.Empty;

                while (await pageReader.ReadAsync())
                {
                    if (pageReader.NodeType == XmlNodeType.Element)
                    {
                        if (pageReader.Name == "title")
                        {
                            title = await pageReader.ReadElementContentAsStringAsync();
                        }
                        else if (pageReader.Name == "text")
                        {
                            text = await pageReader.ReadElementContentAsStringAsync();
                        }
                    }
                }

                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(text))
                {
                    yield return new WikiPageItem
                    {
                        Title = title,
                        Text = text
                    };
                }
            }
        }
    }
}

public class AyaItem
{
    public string Index { get; set; } = string.Empty;
    public string Sura { get; set; } = string.Empty;
    public string AyaNumber { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public class WikiPageItem
{
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
