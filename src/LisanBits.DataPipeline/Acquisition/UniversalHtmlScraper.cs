using HtmlAgilityPack;
using LisanBits.DataPipeline.Data.Models;
using System.Text.RegularExpressions;

namespace LisanBits.DataPipeline.Acquisition;

public class UniversalHtmlScraper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UniversalHtmlScraper> _logger;

    private static readonly string[] UrlBlacklist = new[]
    {
        "donate", "contact", "about", "help", "feedback", "support", 
        "privacy", "terms", "license", "login", "logout", "register", 
        "signup", "signin", "contribution", "social", "facebook", "twitter", 
        "instagram", "youtube", "linkedin", "github"
    };

    public UniversalHtmlScraper(HttpClient httpClient, ILogger<UniversalHtmlScraper> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(15); // Increased timeout to 15 seconds to allow Al Jazeera to load
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "ar,en-US;q=0.9,en;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        _httpClient.DefaultRequestHeaders.Add("sec-ch-ua", "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\", \"Google Chrome\";v=\"120\"");
        _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
        _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
        _httpClient.DefaultRequestHeaders.Add("sec-fetch-dest", "document");
        _httpClient.DefaultRequestHeaders.Add("sec-fetch-mode", "navigate");
        _httpClient.DefaultRequestHeaders.Add("sec-fetch-site", "none");
        _httpClient.DefaultRequestHeaders.Add("sec-fetch-user", "?1");
    }

    public class ScrapeResult 
    {
        /// <summary>Text, sentence count, word count, and resolved taxonomy leaf path (may be null).</summary>
        public List<(string Text, int Sentences, int Words, string? SubContextPath)> ExtractedData { get; set; } = [];
        public HashSet<string> DiscoveredUrls { get; set; } = [];
        public List<LexiconEntry> LexiconEntries { get; set; } = [];
    }

    /// <summary>
    /// Scrapes a generic URL based on the DataSourceConfig.
    /// Returns the Text Content, Sentence Count, Word Count, AND newly discovered URLs.
    /// </summary>
    public async Task<ScrapeResult?> ScrapeAsync(DataSourceConfig config, string url, CancellationToken cancellationToken)
    {
        try
        {
            var result = new ScrapeResult();
            
            // 1. Handle Local File (Quran XML or Slang CSV)
            if (url.StartsWith("file:///"))
            {
                // Strip file:/// prefix and split any ?skip=N query parameter
                var rawPath = Uri.UnescapeDataString(url.Substring("file:///".Length));
                var filePath = rawPath;
                int skipRows = 0;
                const int chunkSize = 1000;
                
                var firstQMark = rawPath.IndexOf('?');
                if (firstQMark >= 0)
                {
                    filePath = rawPath.Substring(0, firstQMark);
                    // Use LastIndexOf to cleanly extract the latest skip=N if there are multiple '?'
                    var query = rawPath.Substring(rawPath.LastIndexOf('?') + 1);
                    foreach (var part in query.Split('&'))
                    {
                        if (part.StartsWith("skip=", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(part.Substring(5), out var s))
                            skipRows = s;
                    }
                }

                if (!File.Exists(filePath))
                {
                    var fileName = Path.GetFileName(filePath);
                    var searchPath = AppContext.BaseDirectory;
                    while (!string.IsNullOrEmpty(searchPath))
                    {
                        var candidate = Path.Combine(searchPath, fileName);
                        if (File.Exists(candidate))
                        {
                            filePath = candidate;
                            break;
                        }
                        searchPath = Path.GetDirectoryName(searchPath);
                    }
                }

                if (!File.Exists(filePath)) return null;

                // Excel file (.xlsx) ingestion for Slang datasets
                if (filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                    int rowsRead = 0;
                    int rowsSkipped = 0;

                    using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream))
                    {
                        bool isHeader = true;

                        while (reader.Read())
                        {
                            if (isHeader)
                            {
                                isHeader = false;
                                continue;
                            }

                            string? textValue = null;
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var val = reader.GetValue(i);
                                if (val != null)
                                {
                                    var valStr = val.ToString()?.Trim();
                                    if (!string.IsNullOrEmpty(valStr))
                                    {
                                        if (textValue == null || valStr.Length > textValue.Length)
                                        {
                                            textValue = valStr;
                                        }
                                    }
                                }
                            }

                            // Skip entirely empty rows so they don't count towards chunking
                            if (string.IsNullOrWhiteSpace(textValue))
                                continue;

                            if (rowsSkipped < skipRows)
                            {
                                rowsSkipped++;
                                continue;
                            }

                            if (rowsRead >= chunkSize)
                                break;

                            var processed = ProcessContent(textValue);
                            if (processed.Sentences > 0)
                            {
                                result.ExtractedData.Add((processed.Text, processed.Sentences, processed.Words, null));
                            }

                            rowsRead++;
                        }

                        if (rowsRead == chunkSize)
                        {
                            var nextSkip = skipRows + chunkSize;
                            result.DiscoveredUrls.Add($"file:///{filePath.Replace('\\', '/')}?skip={nextSkip}");
                        }
                    }

                    return result;
                }

                // CSV_FILE: chunked line-by-line ingestion with queue-based pagination
                if (config.TargetXPath == "CSV_FILE")
                {
                    int linesRead = 0;
                    int linesSkipped = 0;
                    bool isHeader = true;

                    await foreach (var line in ReadLinesAsync(filePath, cancellationToken))
                    {
                        if (isHeader) { isHeader = false; continue; } // skip CSV header
                        
                        if (string.IsNullOrWhiteSpace(line)) continue; // skip entirely empty lines

                        if (linesSkipped < skipRows) { linesSkipped++; continue; }
                        if (linesRead >= chunkSize) break;

                        var processed = ProcessContent(line);
                        if (processed.Sentences > 0)
                            result.ExtractedData.Add((processed.Text, processed.Sentences, processed.Words, null));
                        linesRead++;
                    }

                    // Queue the next chunk if we actually read a full chunk
                    if (linesRead == chunkSize)
                    {
                        var nextSkip = skipRows + chunkSize;
                        result.DiscoveredUrls.Add($"file:///{filePath.Replace('\\', '/')}?skip={nextSkip}");
                    }

                    return result;
                }

                // JSONL_TEXT_FILE: chunked JSON ingestion (handles both JSON Lines and a single large JSON Array)
                if (config.TargetXPath == "JSONL_TEXT_FILE")
                {
                    int itemsRead = 0;
                    int itemsSkipped = 0;

                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                    
                    // Peek first non-whitespace char to determine format
                    int firstChar = -1;
                    while ((firstChar = stream.ReadByte()) != -1)
                    {
                        if (!char.IsWhiteSpace((char)firstChar)) break;
                    }
                    stream.Position = 0; // Reset position

                    if (firstChar == '[')
                    {
                        // JSON Array format
                        var asyncEnumerable = System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable<System.Text.Json.JsonElement>(
                            stream, new System.Text.Json.JsonSerializerOptions { AllowTrailingCommas = true }, cancellationToken);

                        await foreach (var element in asyncEnumerable)
                        {
                            if (itemsSkipped < skipRows) { itemsSkipped++; continue; }
                            if (itemsRead >= chunkSize) break;

                            if (element.ValueKind == System.Text.Json.JsonValueKind.Object && element.TryGetProperty("text", out var textElement))
                            {
                                var textValue = textElement.GetString();
                                if (!string.IsNullOrWhiteSpace(textValue))
                                {
                                    var processed = ProcessContent(textValue);
                                    if (processed.Sentences > 0)
                                        result.ExtractedData.Add((processed.Text, processed.Sentences, processed.Words, null));
                                }
                            }
                            itemsRead++;
                        }
                    }
                    else
                    {
                        // JSON-Lines format
                        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                        while (await reader.ReadLineAsync(cancellationToken) is { } line)
                        {
                            var trimmed = line.Trim();
                            if (string.IsNullOrWhiteSpace(trimmed)) continue;
                            
                            if (itemsSkipped < skipRows) { itemsSkipped++; continue; }
                            if (itemsRead >= chunkSize) break;

                            if (trimmed.EndsWith(",")) trimmed = trimmed[..^1];

                            string textValue = string.Empty;
                            try
                            {
                                if (trimmed.StartsWith("{"))
                                {
                                    using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
                                    if (doc.RootElement.TryGetProperty("text", out var textElement))
                                    {
                                        textValue = textElement.GetString() ?? string.Empty;
                                    }
                                }
                            }
                            catch { /* Ignore malformed lines */ }

                            if (!string.IsNullOrWhiteSpace(textValue))
                            {
                                var processed = ProcessContent(textValue);
                                if (processed.Sentences > 0)
                                    result.ExtractedData.Add((processed.Text, processed.Sentences, processed.Words, null));
                            }

                            itemsRead++;
                        }
                    }

                    if (itemsRead == chunkSize)
                    {
                        var nextSkip = skipRows + chunkSize;
                        result.DiscoveredUrls.Add($"file:///{filePath.Replace('\\', '/')}?skip={nextSkip}");
                    }

                    return result;
                }

                // XML (Quran)
                var xmlText = await File.ReadAllTextAsync(filePath, cancellationToken);
                if (config.TargetXPath == "//aya")
                {
                    var doc = new System.Xml.XmlDocument();
                    doc.LoadXml(xmlText);
                    var nodes = doc.SelectNodes(config.TargetXPath);
                    if (nodes != null)
                    {
                        string content = "";
                        foreach (System.Xml.XmlNode node in nodes)
                        {
                            content += node.Attributes?["text"]?.Value + ".\n";
                        }
                        var qContent = ProcessContent(content);
                        result.ExtractedData.Add((qContent.Text, qContent.Sentences, qContent.Words, null));
                    }
                }
                return result;
            }

            // 2. Fetch the Web Page
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HTTP error {StatusCode} for URL: {Url}", response.StatusCode, url);
                return null;
            }
            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            // If it is a linguistics lexicon, parse structural dictionary entries
            if (config.Category == "Linguistics" || config.Name.Contains("Lexicon"))
            {
                int actualBookId = 0;
                var idMatch = Regex.Match(url, @"/book/(\d+)");
                if (idMatch.Success)
                {
                    int.TryParse(idMatch.Groups[1].Value, out actualBookId);
                }

                if (actualBookId > 0)
                {
                    var parsed = Preprocessing.LexiconParser.ParseHtml(html, actualBookId, config.Name);
                    result.LexiconEntries = parsed;
                }
            }

            var docHtml = new HtmlDocument();
            docHtml.LoadHtml(html);

            // 2.5. Resolve taxonomy sub-context path from page metadata.
            // Priority: Wikipedia category footer → news breadcrumb → null (caller uses DataSourceConfig.Category).
            string? resolvedSubContextPath = null;

            // Wikipedia: extract #mw-normal-catlinks anchor texts
            var catLinksNode = docHtml.DocumentNode.SelectSingleNode("//div[@id='mw-normal-catlinks']//ul");
            if (catLinksNode != null)
            {
                var categoryAnchors = catLinksNode.SelectNodes(".//a")
                    ?.Select(a => System.Net.WebUtility.HtmlDecode(a.InnerText).Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    ?? Enumerable.Empty<string>();

                resolvedSubContextPath = WikiCategoryResolver.ResolveFromWikiCategories(categoryAnchors);
            }

            // News-site fallback: try Arabic section breadcrumbs
            if (resolvedSubContextPath is null)
            {
                var breadcrumbSelectors = new[]
                {
                    "//nav[contains(@class,'breadcrumb')]//a",
                    "//div[contains(@class,'breadcrumb')]//a",
                    "//span[contains(@class,'section')]//a",
                    "//a[contains(@class,'cat')]"
                };
                foreach (var sel in breadcrumbSelectors)
                {
                    var breadcrumbNode = docHtml.DocumentNode.SelectSingleNode(sel);
                    if (breadcrumbNode is null) continue;
                    var label = System.Net.WebUtility.HtmlDecode(breadcrumbNode.InnerText).Trim();
                    resolvedSubContextPath = WikiCategoryResolver.ResolveFromNewsSection(label);
                    if (resolvedSubContextPath is not null) break;
                }
            }

            // 3. Extract Discovered URLs for N-Depth Crawling
            if (!string.IsNullOrEmpty(config.DiscoveryXPath))
            {
                var linkNodes = docHtml.DocumentNode.SelectNodes(config.DiscoveryXPath);
                if (linkNodes != null)
                {
                    var baseUri = new Uri(config.BaseUrl);
                    var currentUri = new Uri(url);
                    foreach (var linkNode in linkNodes)
                    {
                        var href = linkNode.GetAttributeValue("href", "");
                        if (!string.IsNullOrWhiteSpace(href))
                        {
                            if (Uri.TryCreate(currentUri, href, out var absoluteUri))
                            {
                                // Only add URLs that belong to the same host
                                if (absoluteUri.Host == baseUri.Host)
                                {
                                    var normalized = NormalizeUrl(absoluteUri.ToString());
                                    // Filter out blacklisted utility URLs
                                    if (!UrlBlacklist.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        result.DiscoveredUrls.Add(normalized);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 3.5. For Shamela Lexicons, generate the next sequential page URL
            if (config.Category == "Linguistics" && url.Contains("shamela.ws/book/"))
            {
                var pageMatch = Regex.Match(url, @"/book/(\d+)/(\d+)");
                if (pageMatch.Success)
                {
                    int bookId = int.Parse(pageMatch.Groups[1].Value);
                    int page = int.Parse(pageMatch.Groups[2].Value);
                    
                    if (result.LexiconEntries.Count > 0)
                    {
                        result.DiscoveredUrls.Add($"https://shamela.ws/book/{bookId}/{page + 1}");
                    }
                }
                else
                {
                    var baseMatch = Regex.Match(url, @"/book/(\d+)");
                    if (baseMatch.Success)
                    {
                        int bookId = int.Parse(baseMatch.Groups[1].Value);
                        int startPage = bookId switch
                        {
                            1687 => 23,
                            7283 => 9,
                            7030 => 125,
                            7028 => 1,
                            1682 => 50,
                            150964 => 195,
                            _ => 1
                        };
                        result.DiscoveredUrls.Add($"https://shamela.ws/book/{bookId}/{startPage}");
                    }
                }
            }

            // 4. Extract Target Data
            if (config.Category == "Linguistics" || config.Name.Contains("Lexicon"))
            {
                // Structured lexicon parsing already done, skip raw paragraphs extraction
            }
            else if (config.TargetXPath == "CSV")
            {
                // Basic CSV handler
                var lines = html.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines.Skip(1)) // Skip header
                {
                    var p = ProcessContent(line);
                    result.ExtractedData.Add((p.Text, p.Sentences, p.Words, null));
                }
            }
            else
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                // Try primary XPath first, then fall back to semantic HTML tags
                var xpathsToTry = new[] 
                { 
                    config.TargetXPath, 
                    "//article//p", 
                    "//main//p", 
                    "//div[contains(@class,'content')]//p",
                    "//div[contains(@class,'article')]//p",
                    "//body//p"
                };
                
                string content = "";
                foreach (var xpath in xpathsToTry)
                {
                    if (string.IsNullOrEmpty(xpath)) continue;
                    var nodes = doc.DocumentNode.SelectNodes(xpath);
                    if (nodes == null || nodes.Count == 0) continue;
                    
                    var sb = new System.Text.StringBuilder();
                    foreach (var node in nodes)
                    {
                        var text = System.Net.WebUtility.HtmlDecode(node.InnerText).Trim();
                        // Filter: only keep paragraphs with >20 chars that contain Arabic characters
                        if (text.Length > 20 && Regex.IsMatch(text, @"[\u0600-\u06FF]"))
                        {
                            sb.AppendLine(text);
                        }
                    }
                    content = sb.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(content)) break; // Found content, stop trying
                }

                if (!string.IsNullOrWhiteSpace(content))
                {
                    var p = ProcessContent(content);
                    result.ExtractedData.Add((p.Text, p.Sentences, p.Words, resolvedSubContextPath));
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scrape {Url}", url);
            return null;
        }
    }

    private (string Text, int Sentences, int Words) ProcessContent(string content)
    {
        if (IsNoisyContent(content))
        {
            return (content, 0, 0); // 0 sentences signals the caller to discard it
        }

        int words = content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        int sentences = Regex.Split(content, @"(?<=[\.!\?؟\n])\s+").Length;
        return (content, sentences, words);
    }

    private bool IsNoisyContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return true;
        
        // Normalize whitespace to catch cases like "سؤال من      ذكر"
        var normalized = Regex.Replace(content.Trim(), @"\s+", " ");
        
        if (normalized.StartsWith("كل الحقوق محفوظة") ||
            normalized.StartsWith("All rights reserved") ||
            normalized.StartsWith("جميع الحقوق محفوظة") ||
            normalized.StartsWith("تم التصميم والتطوير بواسطة") ||
            normalized.StartsWith("سؤال من ذكر") ||
            normalized.StartsWith("سؤال من أنثى"))
        {
            return true;
        }

        if (normalized.Contains("لم يتم العثور على نتائج") ||
            normalized.Contains("تهدف إلى التثقيف العام فقط") ||
            normalized.Contains("يشتمل هذا التصنيف على") ||
            normalized.Contains("تصنيفا فرعيا، من أصل"))
        {
            return true;
        }

        return false;
    }
    private static async IAsyncEnumerable<string> ReadLinesAsync(
        string filePath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
            yield return line;
    }
    public static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        try
        {
            var uri = new Uri(url);
            var builder = new UriBuilder(uri)
            {
                Fragment = "" // Strip fragment (e.g. #mursal)
            };

            var path = builder.Path;
            if (path.Length > 1 && path.EndsWith("/"))
            {
                builder.Path = path.TrimEnd('/'); // Strip trailing slash
            }
            
            return builder.Uri.ToString();
        }
        catch
        {
            return url;
        }
    }
}
