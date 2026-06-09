using System.ComponentModel.DataAnnotations;

namespace LisanBits.DataPipeline.Data.Models;

public class ProcessedUniversalData
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int RawDataId { get; set; }

    [Required]
    public string ProcessedText { get; set; } = string.Empty;

    public string RootSequence { get; set; } = string.Empty;

    public string PosSequence { get; set; } = string.Empty;

    public string ContextVector { get; set; } = "{}"; // JSON dictionary mapping Topic -> Probability

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
