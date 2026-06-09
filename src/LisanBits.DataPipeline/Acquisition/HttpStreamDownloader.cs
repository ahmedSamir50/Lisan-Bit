namespace LisanBits.DataPipeline.Acquisition;

/// <summary>
/// Handles highly optimized, streaming downloads of large files to avoid loading payloads into memory.
/// </summary>
public class HttpStreamDownloader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpStreamDownloader> _logger;

    public HttpStreamDownloader(HttpClient httpClient, ILogger<HttpStreamDownloader> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Downloads a large file via HTTP stream and saves it directly to disk.
    /// </summary>
    public async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting streaming download from {Url} to {DestinationPath}", url, destinationPath);

        // Ensure directory exists
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Use HttpCompletionOption.ResponseHeadersRead to stream the body
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        
        // Use a 81920 byte buffer (80KB is optimal for Large Object Heap avoidance on most systems when using default buffer size for FileStream)
        // Actually, FileStream defaults to 4096. We'll use 81920 (80*1024) which is just under the 85,000 byte LOH threshold.
        await using var fileStream = new FileStream(
            destinationPath, 
            FileMode.Create, 
            FileAccess.Write, 
            FileShare.None, 
            bufferSize: 81920, 
            useAsync: true);

        await contentStream.CopyToAsync(fileStream, cancellationToken);

        _logger.LogInformation("Download completed successfully.");
    }
}
