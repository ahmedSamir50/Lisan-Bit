using System.Text;
using Microsoft.Data.Sqlite;
using TorchSharp;
using static TorchSharp.torch;

namespace LisanBits.Trainer;

public static class Program
{
    private const string DatabasePath = @"D:\A_S\pipeline.db";
    private const string OutputModelPath = @"D:\A_S\ternary_embeddings.bin";
    private const int EmbeddingDim = 128;
    private const int WindowSize = 5;
    private const int NegativeSamples = 5;
    private const int BatchSize = 4096;
    private const int Epochs = 5;
    private const double LearningRate = 0.025;
    private const int MinCount = 3;

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== Lisan Bits - Skip-gram TorchSharp Trainer ===");

        if (!File.Exists(DatabasePath))
        {
            Console.WriteLine($"Error: SQLite Database not found at: {DatabasePath}");
            return;
        }

        // 1. Load sentences and build vocabulary
        Console.WriteLine("Loading training corpus from SQLite...");
        var rawSentences = await LoadSentencesFromDbAsync();
        Console.WriteLine($"Loaded {rawSentences.Count:N0} processed rows.");

        Console.WriteLine("Normalizing and tokenizing...");
        var tokenizedCorpus = new List<string[]>(rawSentences.Count);
        var wordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var text in rawSentences)
        {
            var cleanText = StripTashkeelAndNormalize(text);
            var tokens = cleanText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Where(t => t.Length > 1) // skip single letters
                                  .ToArray();
            
            if (tokens.Length > 0)
            {
                tokenizedCorpus.Add(tokens);
                foreach (var token in tokens)
                {
                    wordCounts[token] = wordCounts.GetValueOrDefault(token) + 1;
                }
            }
        }

        Console.WriteLine($"Found {wordCounts.Count:N0} unique raw tokens.");

        // Build Vocab with MinCount threshold
        var vocabList = new List<string> { "<UNK>" };
        foreach (var (word, count) in wordCounts.OrderByDescending(kv => kv.Value))
        {
            if (count >= MinCount && word != "<UNK>")
            {
                vocabList.Add(word);
            }
        }

        var vocab = vocabList.Select((w, idx) => (w, idx)).ToDictionary(x => x.w, x => x.idx, StringComparer.OrdinalIgnoreCase);
        var vocabSize = vocabList.Count;
        Console.WriteLine($"Filtered Vocabulary Size: {vocabSize:N0} (MinCount: {MinCount})");

        // Convert corpus to indices
        var indexedCorpus = new List<int[]>(tokenizedCorpus.Count);
        foreach (var sentence in tokenizedCorpus)
        {
            var indices = sentence.Select(w => vocab.TryGetValue(w, out var id) ? id : 0).ToArray();
            if (indices.Length > 1)
            {
                indexedCorpus.Add(indices);
            }
        }

        // 2. Prepare negative sampling distribution (Unigram raised to 0.75)
        Console.WriteLine("Building negative sampling table...");
        var noiseDistribution = new double[vocabSize];
        double totalNoiseWeight = 0;
        for (int i = 0; i < vocabSize; i++)
        {
            var word = vocabList[i];
            var count = wordCounts.GetValueOrDefault(word, 0);
            var weight = Math.Pow(count, 0.75);
            noiseDistribution[i] = weight;
            totalNoiseWeight += weight;
        }

        // Build discrete sampler
        var cumulativeDistribution = new double[vocabSize];
        double runningSum = 0;
        for (int i = 0; i < vocabSize; i++)
        {
            runningSum += noiseDistribution[i] / totalNoiseWeight;
            cumulativeDistribution[i] = runningSum;
        }

        var random = new Random(42);
        int DrawNegativeSample()
        {
            double r = random.NextDouble();
            int low = 0, high = vocabSize - 1;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (r < cumulativeDistribution[mid])
                {
                    high = mid;
                }
                else
                {
                    low = mid + 1;
                }
            }
            return low;
        }

        // 3. Generate target-context training pairs
        Console.WriteLine("Generating training pairs...");
        var targetIds = new List<int>();
        var posIds = new List<int>();
        var negIds = new List<int[]>();

        foreach (var sentence in indexedCorpus)
        {
            for (int i = 0; i < sentence.Length; i++)
            {
                int target = sentence[i];
                int start = Math.Max(0, i - WindowSize / 2);
                int end = Math.Min(sentence.Length - 1, i + WindowSize / 2);

                for (int j = start; j <= end; j++)
                {
                    if (i == j) continue;
                    int pos = sentence[j];

                    // Draw negative samples
                    var negs = new int[NegativeSamples];
                    for (int n = 0; n < NegativeSamples; n++)
                    {
                        int sampled;
                        do
                        {
                            sampled = DrawNegativeSample();
                        } while (sampled == target || sampled == pos);
                        negs[n] = sampled;
                    }

                    targetIds.Add(target);
                    posIds.Add(pos);
                    negIds.Add(negs);
                }
            }
        }

        var totalPairs = targetIds.Count;
        Console.WriteLine($"Total target-context pairs generated: {totalPairs:N0}");

        // 4. Implement Skip-gram model using TorchSharp
        Console.WriteLine("Initializing TorchSharp Skip-gram model...");
        var model = new SkipGramModel(vocabSize, EmbeddingDim);
        var optimizer = optim.Adam(model.parameters(), lr: LearningRate);

        Console.WriteLine($"Starting training for {Epochs} epochs (Batch Size: {BatchSize})...");
        int stepCount = (totalPairs + BatchSize - 1) / BatchSize;

        for (int epoch = 1; epoch <= Epochs; epoch++)
        {
            double epochLoss = 0;
            int processed = 0;

            // Shuffle indices
            var indices = Enumerable.Range(0, totalPairs).ToArray();
            // Shuffle using Fisher-Yates
            for (int i = totalPairs - 1; i > 0; i--)
            {
                int k = random.Next(i + 1);
                var temp = indices[i];
                indices[i] = indices[k];
                indices[k] = temp;
            }

            for (int batchIdx = 0; batchIdx < stepCount; batchIdx++)
            {
                int take = Math.Min(BatchSize, totalPairs - processed);
                if (take <= 0) break;

                var targetBatch = new int[take];
                var posBatch = new int[take];
                var negBatchFlat = new int[take * NegativeSamples];

                for (int i = 0; i < take; i++)
                {
                    int idx = indices[processed + i];
                    targetBatch[i] = targetIds[idx];
                    posBatch[i] = posIds[idx];
                    Array.Copy(negIds[idx], 0, negBatchFlat, i * NegativeSamples, NegativeSamples);
                }

                using var targetTensor = tensor(targetBatch, dtype: ScalarType.Int64);
                using var posTensor = tensor(posBatch, dtype: ScalarType.Int64);
                using var negTensor = tensor(negBatchFlat, dtype: ScalarType.Int64).view(take, NegativeSamples);

                optimizer.zero_grad();
                using var loss = model.forward(targetTensor, posTensor, negTensor);
                loss.backward();
                optimizer.step();

                epochLoss += loss.item<float>() * take;
                processed += take;
            }

            double averageLoss = epochLoss / totalPairs;
            Console.WriteLine($"Epoch {epoch}/{Epochs} completed. Average Loss: {averageLoss:F4}");
        }

        // 5. Post-Training Quantization (PTQ) & Ternary Mapping
        Console.WriteLine("Quantizing embeddings to Ternary (-1, 0, 1)...");
        var weightsTensor = model.GetWeights();
        var weights = weightsTensor.data<float>().ToArray(); // size: vocabSize * EmbeddingDim

        // Compute gamma (mean absolute value)
        double totalAbs = 0;
        foreach (var w in weights)
        {
            totalAbs += Math.Abs(w);
        }
        float gamma = (float)(totalAbs / weights.Length);
        float delta = 0.7f * gamma;

        Console.WriteLine($"Scaling factor (gamma): {gamma:F5}");
        Console.WriteLine($"Quantization threshold (delta): {delta:F5}");

        var ternaryWeights = new sbyte[weights.Length];
        var bias = new float[vocabSize];

        for (int i = 0; i < vocabSize; i++)
        {
            double rowErrorSum = 0;

            for (int j = 0; j < EmbeddingDim; j++)
            {
                int idx = i * EmbeddingDim + j;
                float w = weights[idx];

                sbyte tVal;
                if (w > delta) tVal = 1;
                else if (w < -delta) tVal = -1;
                else tVal = 0;

                ternaryWeights[idx] = tVal;

                // Reconstruct weight and add to error sum
                float reconstructed = gamma * tVal;
                rowErrorSum += (w - reconstructed);
            }

            // Standard scale approximation: bias is average row quantization error
            bias[i] = (float)(rowErrorSum / EmbeddingDim);
        }

        // 6. Binary serialization to flat binary format
        Console.WriteLine($"Exporting model to raw binary: {OutputModelPath}...");
        using (var fileStream = new FileStream(OutputModelPath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(fileStream, Encoding.UTF8))
        {
            // Signature / Magic bytes
            writer.Write("LISANBITS_EMB");
            writer.Write(vocabSize);
            writer.Write(EmbeddingDim);
            writer.Write(gamma);

            // Vocabulary words
            for (int i = 0; i < vocabSize; i++)
            {
                writer.Write(vocabList[i]);
            }

            // Bias vector
            for (int i = 0; i < vocabSize; i++)
            {
                writer.Write(bias[i]);
            }

            // Quantized Ternary sbyte matrix
            for (int i = 0; i < weights.Length; i++)
            {
                writer.Write(ternaryWeights[i]);
            }
        }

        Console.WriteLine("Quantized ternary embeddings exported successfully!");
        Console.WriteLine($"File size: {new FileInfo(OutputModelPath).Length / 1024.0:N2} KB");
    }

    private static async Task<List<string>> LoadSentencesFromDbAsync()
    {
        var sentences = new List<string>();
        using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();

        var query = "SELECT ProcessedText FROM ProcessedUniversalData";
        using var command = new SqliteCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            var text = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(text))
            {
                sentences.Add(text);
            }
        }
        return sentences;
    }

    private static string StripTashkeelAndNormalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sb = new StringBuilder();
        foreach (char c in text)
        {
            // Skip diacritics
            if ((c >= 0x064B && c <= 0x065F) || c == 0x0670)
            {
                continue;
            }
            sb.Append(c);
        }

        var normalized = sb.ToString();
        // Normalize Alefs
        normalized = normalized.Replace('أ', 'ا')
                               .Replace('إ', 'ا')
                               .Replace('آ', 'ا');
        // Normalize Teh Marbuta
        normalized = normalized.Replace('ة', 'ه');
        // Normalize Yeh
        normalized = normalized.Replace('ى', 'ي');

        return normalized;
    }
}

public class SkipGramModel : torch.nn.Module<torch.Tensor, torch.Tensor, torch.Tensor, torch.Tensor>
{
    private readonly TorchSharp.Modules.Embedding targetEmbeddings;
    private readonly TorchSharp.Modules.Embedding contextEmbeddings;

    public SkipGramModel(long vocabSize, long embeddingDim) : base("skipgram")
    {
        targetEmbeddings = nn.Embedding(vocabSize, embeddingDim);
        contextEmbeddings = nn.Embedding(vocabSize, embeddingDim);
        
        // Initialize weights using standard normal distribution (small standard dev)
        targetEmbeddings.weight!.normal_(0.0, 1.0 / Math.Sqrt(embeddingDim));
        contextEmbeddings.weight!.normal_(0.0, 1.0 / Math.Sqrt(embeddingDim));

        RegisterComponents();
    }

    public override torch.Tensor forward(torch.Tensor target, torch.Tensor posContext, torch.Tensor negContext)
    {
        // Target embeddings: (batch, dim)
        using var targetEmbed = targetEmbeddings.forward(target);
        // Positive context embeddings: (batch, dim)
        using var posEmbed = contextEmbeddings.forward(posContext);
        // Negative context embeddings: (batch, neg_samples, dim)
        using var negEmbed = contextEmbeddings.forward(negContext);

        // Positive scores: dot products of targets and posContexts (shape: batch)
        using var posScore = (targetEmbed * posEmbed).sum(new long[] { 1 });
        
        // Negative scores: dot products of targets and negContexts (shape: batch x neg_samples)
        using var targetExpanded = targetEmbed.unsqueeze(1).expand(target.shape[0], negContext.shape[1], targetEmbed.shape[1]);
        using var negScore = (targetExpanded * negEmbed).sum(new long[] { 2 });

        // Calculate binary cross entropy loss
        using var posLoss = -log(sigmoid(posScore));
        using var negLoss = -log(sigmoid(-negScore)).sum(new long[] { 1 });

        return (posLoss + negLoss).mean();
    }

    public torch.Tensor GetWeights()
    {
        return targetEmbeddings.weight!;
    }
}
