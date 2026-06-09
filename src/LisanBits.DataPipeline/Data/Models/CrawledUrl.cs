using System.ComponentModel.DataAnnotations;

namespace LisanBits.DataPipeline.Data.Models;

public class CrawledUrl
{
    [Key]
    public int Id { get; set; }
    
    public int DataSourceId { get; set; }
    
    [Required]
    public string Url { get; set; } = string.Empty;
    
    public string Status { get; set; } = "Pending"; // Pending, Completed, Failed
    
    public int Depth { get; set; } = 0;
    
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? ProcessedAt { get; set; }
}
