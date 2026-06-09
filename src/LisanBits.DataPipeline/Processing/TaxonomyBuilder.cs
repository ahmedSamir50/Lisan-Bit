using System.Text.RegularExpressions;

namespace LisanBits.DataPipeline.Processing;

/// <summary>
/// Extracts taxonomy/categories from Arabic Wikipedia dumps using zero-allocation Source-Generated Regex.
/// </summary>
public partial class TaxonomyBuilder
{
    // Extracts [[تصنيف:CategoryName]] or [[Category:CategoryName]]
    [GeneratedRegex(@"\[\[(?:تصنيف|Category):([^\]|]+)(?:\|[^\]]*)?\]\]", RegexOptions.Compiled)]
    private static partial Regex CategoryRegex();

    /// <summary>
    /// Parses a Wikipedia page text and returns a list of its categories.
    /// </summary>
    public static List<string> ExtractCategories(string textContent)
    {
        var categories = new List<string>();
        
        if (string.IsNullOrEmpty(textContent))
            return categories;

        var matches = CategoryRegex().Matches(textContent);
        
        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                categories.Add(match.Groups[1].Value.Trim());
            }
        }

        return categories;
    }
}
