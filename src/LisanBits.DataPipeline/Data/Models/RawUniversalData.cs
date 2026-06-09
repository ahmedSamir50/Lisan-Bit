namespace LisanBits.DataPipeline.Data.Models;

public class RawUniversalData
{
    public int Id { get; set; }
    public string Category { get; set; } = string.Empty; // e.g. Science, Medical, Religion (from DataSourceConfig)
    public string? SubContextPath { get; set; }          // resolved taxonomy leaf path e.g. "Science/Medicine/Cardiology" (may be null)
    public string Source { get; set; } = string.Empty;   // e.g. Altibbi, Wikipedia
    public string TextContent { get; set; } = string.Empty;
    public int SentenceCount { get; set; }
    public int WordCount { get; set; }
    public DateTime ScrapedAt { get; set; }
}
