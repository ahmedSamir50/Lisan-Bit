namespace LisanBits.DataPipeline.Data.Models;

public class LexiconEntry
{
    public int Id { get; set; }
    public string Word { get; set; } = string.Empty;
    public string Root { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
    public string Synonyms { get; set; } = string.Empty; // Comma separated
    public string Antonyms { get; set; } = string.Empty; // Comma separated
    public string Plurals { get; set; } = string.Empty;  // Comma separated
    public string SourceBook { get; set; } = string.Empty; // e.g. Lisan al-Arab
}
