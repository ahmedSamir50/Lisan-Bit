using System.Runtime.CompilerServices;

namespace LisanBits.DataPipeline.Parsing;

public class SlangDatasetParser
{
    public async IAsyncEnumerable<string> ParseDatasetAsync(string filePath, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Simple line-by-line reading for massive CSV/TXT datasets
        using var streamReader = new StreamReader(
            new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
        );

        while (await streamReader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Simplified: Assuming raw text or single column CSV for prototype
            yield return line.Trim();
        }
    }
}
