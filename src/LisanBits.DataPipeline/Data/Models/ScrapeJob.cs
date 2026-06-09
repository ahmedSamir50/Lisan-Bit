namespace LisanBits.DataPipeline.Data.Models;

public class ScrapeJob
{
    public int Id { get; set; }
    public string SourceName { get; set; } = string.Empty; // e.g., "Sunnah", "Shamela"
    public string TargetId { get; set; } = string.Empty; // e.g., "Bukhari", "7030"
    public int LastProcessedIndex { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, InProgress, Completed, Failed
    public DateTime LastUpdatedAt { get; set; }
}
