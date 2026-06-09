using System.Net.Http.Json;

namespace LisanBits.DataPipeline.Processing;

public class FarasaClient
{
    private readonly HttpClient _httpClient;

    public FarasaClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<TokenAnalysis>> AnalyzeTextAsync(string text, CancellationToken cancellationToken = default)
    {
        var request = new { text = text };
        var response = await _httpClient.PostAsJsonAsync("/analyze", request, cancellationToken);
        
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AnalysisResponse>(cancellationToken: cancellationToken);
        
        return result?.Tokens ?? new List<TokenAnalysis>();
    }
}

public class AnalysisResponse
{
    public List<TokenAnalysis> Tokens { get; set; } = new();
}

public class TokenAnalysis
{
    public string Word { get; set; } = string.Empty;
    public string Root { get; set; } = string.Empty;
    public string Pos { get; set; } = string.Empty;
}
