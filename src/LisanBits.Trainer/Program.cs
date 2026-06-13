using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TorchSharp;
using static TorchSharp.torch;

namespace LisanBits.Trainer;

public class TrainRequest
{
    [JsonPropertyName("vocab_size")]
    public int VocabSize { get; set; }

    [JsonPropertyName("dim")]
    public int Dim { get; set; }

    [JsonPropertyName("epochs")]
    public int Epochs { get; set; }

    [JsonPropertyName("lr")]
    public double Lr { get; set; }
}

public class TrainerStatus
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "Idle";

    [JsonPropertyName("epochs")]
    public int Epochs { get; set; }

    [JsonPropertyName("current_epoch")]
    public int CurrentEpoch { get; set; }

    [JsonPropertyName("loss")]
    public double Loss { get; set; }

    [JsonPropertyName("logs")]
    public List<string> Logs { get; set; } = new();
}

public static class Program
{
    private const string DatabasePath = @"D:\A_S\pipeline.db";
    private const string LocalCheckpointPath = @"D:\A_S\checkpoints\model.pt";
    private const int EmbeddingDim = 128;
    private const int VocabSize = 1000;
    private const int Epochs = 5;
    private const double LearningRate = 0.025;

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== Lisan Bits - Sidecar HTTP Trainer Orchestrator ===");

        // 1. Load configuration and resolve endpoints
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var trainerBaseUrl = configuration["services:lisanbits-trainer-api:trainer-endpoint:0"]
                          ?? "http://localhost:8000";

        Console.WriteLine($"Resolved python trainer endpoint to: {trainerBaseUrl}");

        using var httpClient = new HttpClient();
        httpClient.BaseAddress = new Uri(trainerBaseUrl);
        httpClient.Timeout = TimeSpan.FromMinutes(15);

        // 2. Export and Upload Corpus
        Console.WriteLine($"\nStep 1: Reading corpus from SQLite database ({DatabasePath})...");
        if (!File.Exists(DatabasePath))
        {
            Console.WriteLine($"Warning: SQLite Database not found at: {DatabasePath}. Will proceed with default container corpus.");
        }
        else
        {
            try
            {
                var sentences = await LoadSentencesFromDbAsync();
                Console.WriteLine($"Loaded {sentences.Count:N0} processed rows from database.");
                
                if (sentences.Count > 0)
                {
                    var fullCorpusText = string.Join("\n", sentences);
                    Console.WriteLine("Uploading corpus to trainer container...");
                    var uploadContent = new StringContent(fullCorpusText, Encoding.UTF8, "text/plain");
                    var uploadResponse = await httpClient.PostAsync("/upload-corpus", uploadContent);
                    uploadResponse.EnsureSuccessStatusCode();
                    Console.WriteLine("Corpus uploaded successfully to trainer container.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exporting/uploading corpus: {ex.Message}. Container fallback corpus will be used.");
            }
        }

        // 3. Trigger Training
        Console.WriteLine("\nStep 2: Triggering training task inside container...");
        var trainRequest = new TrainRequest
        {
            VocabSize = VocabSize,
            Dim = EmbeddingDim,
            Epochs = Epochs,
            Lr = LearningRate
        };

        var requestJson = JsonSerializer.Serialize(trainRequest);
        var trainContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

        try
        {
            var trainResponse = await httpClient.PostAsync("/train", trainContent);
            trainResponse.EnsureSuccessStatusCode();
            Console.WriteLine("Training trigger accepted. Monitoring training loop...");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting training: {ex.Message}");
            return;
        }

        // 4. Poll and monitor training status
        int lastPrintedLogIndex = 0;
        bool isDone = false;

        while (!isDone)
        {
            await Task.Delay(1000);
            try
            {
                var status = await httpClient.GetFromJsonAsync<TrainerStatus>("/status");
                if (status == null) continue;

                // Print new logs
                if (status.Logs.Count > lastPrintedLogIndex)
                {
                    for (int i = lastPrintedLogIndex; i < status.Logs.Count; i++)
                    {
                        Console.WriteLine($"[CONTAINER LOG] {status.Logs[i]}");
                    }
                    lastPrintedLogIndex = status.Logs.Count;
                }

                if (status.Status == "Completed")
                {
                    Console.WriteLine("\nContainer training completed successfully.");
                    isDone = true;
                }
                else if (status.Status == "Failed")
                {
                    Console.WriteLine("\nContainer training task failed.");
                    isDone = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error polling status: {ex.Message}");
            }
        }

        // 5. Download model checkpoint
        Console.WriteLine($"\nStep 3: Downloading checkpoint to {LocalCheckpointPath}...");
        try
        {
            var modelDir = Path.GetDirectoryName(LocalCheckpointPath);
            if (!string.IsNullOrEmpty(modelDir))
            {
                Directory.CreateDirectory(modelDir);
            }

            var modelStream = await httpClient.GetStreamAsync("/model");
            using (var fileStream = File.Create(LocalCheckpointPath))
            {
                await modelStream.CopyToAsync(fileStream);
            }
            Console.WriteLine($"Checkpoint downloaded successfully. File size: {new FileInfo(LocalCheckpointPath).Length / 1024.0:N2} KB");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading checkpoint: {ex.Message}");
            return;
        }

        // 6. TorchSharp validation of model checkpoint
        Console.WriteLine("\nStep 4: Validating downloaded model checkpoint using TorchSharp...");
        try
        {
            var device = torch.cuda.is_available() ? torch.CUDA : torch.CPU;
            Console.WriteLine($"Loading TorchScript module on {device.type}...");

            using (var module = torch.jit.load(LocalCheckpointPath))
            {
                module.to(device);
                Console.WriteLine("TorchScript model loaded successfully.");

                // Prepare test input: 3 dummy token IDs
                using (var inputTensor = torch.tensor(new long[] { 0, 1, 2 }, dtype: ScalarType.Int64, device: device))
                {
                    Console.WriteLine($"Input tensor shape: {string.Join("x", inputTensor.shape)}");
                    
                    // Run verification inference
                    using (var outputTensor = (torch.Tensor)module.forward(inputTensor))
                    {
                        Console.WriteLine($"Verification inference successful!");
                        Console.WriteLine($"Output tensor shape: {string.Join("x", outputTensor.shape)} (Expected: 3x{VocabSize})");
                        
                        var floatArray = outputTensor.cpu().data<float>().ToArray();
                        Console.WriteLine($"First few logit outputs: [{string.Join(", ", floatArray.Take(5).Select(x => x.ToString("F4")))}...]");
                    }
                }
            }

            Console.WriteLine("\n=== Validation and container orchestration complete! ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error validating checkpoint: {ex.Message}");
        }
    }

    private static async Task<List<string>> LoadSentencesFromDbAsync()
    {
        var sentences = new List<string>();
        using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();

            var query = "SELECT ProcessedText FROM ProcessedUniversalData";
            using (var command = new SqliteCommand(query, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var text = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sentences.Add(text);
                    }
                }
            }
        }
        return sentences;
    }
}
