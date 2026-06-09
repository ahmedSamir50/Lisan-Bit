namespace LisanBits.DataPipeline.Data.Models;

public class DataSourceConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string TargetXPath { get; set; } = string.Empty;
    public string? LinkXPath { get; set; }
    public string? DiscoveryXPath { get; set; }
    public string PaginationParam { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
