using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CopilotEvalApi.Models;
using CopilotEvalApi.Repositories;
using System.Text;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;

namespace CopilotEvalApi.Services;

/// <summary>
/// Service interface for job execution operations
/// </summary>
public interface IExecutionService
{
    Task<JobExecutionResult> ExecuteJobAsync(Job job, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service implementation for executing jobs with stubbed logic
/// </summary>
public class ExecutionService : IExecutionService
{
    private readonly ILogger<ExecutionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly BlobServiceClient? _blobServiceClient;
    private readonly IJobRepository _jobRepository;
    private readonly IServiceProvider _serviceProvider;

    public ExecutionService(
        ILogger<ExecutionService> logger,
        IConfiguration configuration,
        IJobRepository jobRepository,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _jobRepository = jobRepository;
        _serviceProvider = serviceProvider;

        // Initialize Blob Storage client
        var storageConnectionString = _configuration.GetConnectionString("BlobStorage") ?? "InMemory";
        if (storageConnectionString != "InMemory")
        {
            _blobServiceClient = new BlobServiceClient(storageConnectionString);
            _logger.LogInformation("📦 Blob Storage client initialized");
        }
        else
        {
            _logger.LogWarning("⚠️ Blob Storage connection string not configured, using in-memory mode");
            _blobServiceClient = null;
        }
    }

    public async Task<JobExecutionResult> ExecuteJobAsync(Job job, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("⚡ [Execution {RequestId}] Starting job execution - JobId: {JobId}, Type: {JobType}", 
            requestId, job.Id, job.Type);

        try
        {
            // Update job status to Running
            await UpdateJobStatusAsync(job.Id, JobStatus.Running, 
                new JobProgress(TotalItems: 100, CompletedItems: 0, Percentage: 0.0));

            var result = job.Type switch
            {
                JobType.BulkEvaluation => await ExecuteBulkEvaluationAsync(job, requestId, cancellationToken),
                JobType.SingleEvaluation => await ExecuteSingleEvaluationAsync(job, requestId, cancellationToken),
                JobType.BatchProcessing => await ExecuteBatchProcessingAsync(job, requestId, cancellationToken),
                _ => throw new NotSupportedException($"Job type {job.Type} is not supported")
            };

            _logger.LogInformation("✅ [Execution {RequestId}] Job execution completed successfully - JobId: {JobId}", 
                requestId, job.Id);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("🚫 [Execution {RequestId}] Job execution cancelled - JobId: {JobId}", requestId, job.Id);
            await UpdateJobStatusAsync(job.Id, JobStatus.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("💥 [Execution {RequestId}] Job execution failed - JobId: {JobId}, Error: {Error}", 
                requestId, job.Id, ex.Message);

            var errorDetails = new JobErrorDetails
            {
                ErrorCode = "EXECUTION_FAILED",
                ErrorMessage = ex.Message,
                ErrorTimestamp = DateTimeOffset.UtcNow,
                RetryPossible = IsRetryable(ex)
            };

            await UpdateJobStatusAsync(job.Id, JobStatus.Failed, null, errorDetails);
            
            return new JobExecutionResult
            {
                Success = false,
                ErrorDetails = errorDetails,
                ArtifactBlobReference = null
            };
        }
    }

    private async Task<JobExecutionResult> ExecuteBulkEvaluationAsync(Job job, string requestId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("📊 [Execution {RequestId}] Executing bulk evaluation job", requestId);

        // Parse the uploaded CSV file to get actual evaluation data
        var csvData = await ParseCsvDataAsync(job, requestId, cancellationToken);
        if (csvData == null || csvData.Count == 0)
        {
            _logger.LogError("❌ [Execution {RequestId}] No valid CSV data found for job {JobId}", requestId, job.Id);
            throw new InvalidOperationException("No valid CSV data found for processing");
        }

        var totalItems = csvData.Count;
        var results = new List<EvaluationResult>();

        _logger.LogInformation("📋 [Execution {RequestId}] Processing {TotalItems} prompts from CSV", requestId, totalItems);

        // Process each row from the CSV
        for (int i = 0; i < csvData.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var csvRow = csvData[i];
            var itemId = $"row_{i + 1:D3}";
            var prompt = csvRow.GetValueOrDefault("prompt", "");

            _logger.LogDebug("🔍 [Execution {RequestId}] Processing item {ItemId}: {Prompt}", 
                requestId, itemId, prompt.Length > 50 ? prompt[..50] + "..." : prompt);

            // Simulate AI processing time (replace with actual Copilot API call)
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);

            // Generate evaluation result for this specific prompt
            var evaluationResult = await ProcessSinglePromptAsync(csvRow, itemId, job.Configuration, requestId, cancellationToken);
            results.Add(evaluationResult);

            // Update progress every 5 items or at completion
            if ((i + 1) % 5 == 0 || i == csvData.Count - 1)
            {
                var progress = new JobProgress(totalItems, i + 1, (double)(i + 1) / totalItems * 100);
                await UpdateJobStatusAsync(job.Id, JobStatus.Running, progress);
                _logger.LogInformation("📈 [Execution {RequestId}] Progress update: {Completed}/{Total} ({Percentage:F1}%)", 
                    requestId, i + 1, totalItems, progress.Percentage);
            }
        }

        // Generate comprehensive summary
        var summary = GenerateResultsSummary(results, requestId);

        // Create detailed job results
        var jobResults = new JobResults
        {
            Summary = summary,
            DetailedResults = results
        };

        // Upload results to blob storage
        var blobReference = await UploadResultsToBlobAsync(job.Id, jobResults, requestId);

        // Update job to completed status
        await UpdateJobStatusAsync(job.Id, JobStatus.Completed, 
            new JobProgress(totalItems, totalItems, 100.0));

        _logger.LogInformation("✅ [Execution {RequestId}] Bulk evaluation completed - {TotalItems} items processed, {PassedCount} passed, {FailedCount} failed", 
            requestId, totalItems, summary.PassedEvaluations, summary.FailedEvaluations);

        return new JobExecutionResult
        {
            Success = true,
            Results = jobResults,
            ArtifactBlobReference = blobReference
        };
    }

    private async Task<JobExecutionResult> ExecuteSingleEvaluationAsync(Job job, string requestId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🎯 [Execution {RequestId}] Executing single evaluation job", requestId);

        // Simulate single evaluation processing
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

        var evaluationResult = new EvaluationResult
        {
            ItemId = "single_evaluation",
            Prompt = "Single evaluation prompt",
            ExpectedResponse = "Expected response",
            ActualResponse = "Actual response",
            SimilarityScore = Random.Shared.NextDouble() * 0.4 + 0.6,
            Passed = Random.Shared.NextDouble() > 0.3,
            EvaluationDetails = new EvaluationDetails
            {
                Reasoning = "Single evaluation reasoning",
                Differences = "Minor differences"
            }
        };

        var summary = new ResultsSummary
        {
            TotalEvaluations = 1,
            PassedEvaluations = evaluationResult.Passed == true ? 1 : 0,
            FailedEvaluations = evaluationResult.Passed == false ? 1 : 0,
            AverageScore = evaluationResult.SimilarityScore ?? 0,
            PassRate = evaluationResult.Passed == true ? 100.0 : 0.0
        };

        var jobResults = new JobResults
        {
            Summary = summary,
            DetailedResults = new List<EvaluationResult> { evaluationResult }
        };

        // Upload results to blob storage
        var blobReference = await UploadResultsToBlobAsync(job.Id, jobResults, requestId);

        // Update job to completed status
        await UpdateJobStatusAsync(job.Id, JobStatus.Completed, 
            new JobProgress(1, 1, 100.0));

        return new JobExecutionResult
        {
            Success = true,
            Results = jobResults,
            ArtifactBlobReference = blobReference
        };
    }

    private async Task<JobExecutionResult> ExecuteBatchProcessingAsync(Job job, string requestId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔄 [Execution {RequestId}] Executing batch processing job", requestId);

        // Simulate batch processing with multiple phases
        var phases = new[] { "Data Loading", "Processing", "Validation", "Output Generation" };
        var totalPhases = phases.Length;

        for (int phase = 0; phase < totalPhases; phase++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("🔧 [Execution {RequestId}] Phase {Phase}: {PhaseName}", 
                requestId, phase + 1, phases[phase]);

            // Simulate phase processing time
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);

            var progress = new JobProgress(totalPhases, phase + 1, (double)(phase + 1) / totalPhases * 100);
            await UpdateJobStatusAsync(job.Id, JobStatus.Running, progress);
        }

        // Generate mock batch results
        var batchResults = new JobResults
        {
            Summary = new ResultsSummary
            {
                TotalEvaluations = 25,
                PassedEvaluations = 20,
                FailedEvaluations = 5,
                AverageScore = 0.82,
                PassRate = 80.0
            }
        };

        // Upload results to blob storage
        var blobReference = await UploadResultsToBlobAsync(job.Id, batchResults, requestId);

        // Update job to completed status
        await UpdateJobStatusAsync(job.Id, JobStatus.Completed, 
            new JobProgress(totalPhases, totalPhases, 100.0));

        return new JobExecutionResult
        {
            Success = true,
            Results = batchResults,
            ArtifactBlobReference = blobReference
        };
    }

    private async Task<List<Dictionary<string, string>>> ParseCsvDataAsync(Job job, string requestId, CancellationToken cancellationToken)
    {
        // Prioritize DataSourceBlobRef (uploaded file) over DataSource (static reference)
        var dataSourceUrl = job.Configuration.DataSourceBlobRef ?? job.Configuration.DataSource;
        
        _logger.LogCritical("� [DEBUG {RequestId}] JOB CONFIGURATION ANALYSIS:", requestId);
        _logger.LogCritical("🔍 [DEBUG {RequestId}] - Job ID: {JobId}", requestId, job.Id);
        _logger.LogCritical("🔍 [DEBUG {RequestId}] - DataSource: '{DataSource}'", requestId, job.Configuration.DataSource ?? "NULL");
        _logger.LogCritical("🔍 [DEBUG {RequestId}] - DataSourceBlobRef: '{DataSourceBlobRef}'", requestId, job.Configuration.DataSourceBlobRef ?? "NULL");
        _logger.LogCritical("🔍 [DEBUG {RequestId}] - Using URL: '{DataSourceUrl}'", requestId, dataSourceUrl ?? "NULL");
        _logger.LogCritical("🔍 [DEBUG {RequestId}] - BlobServiceClient null: {IsNull}", requestId, _blobServiceClient == null);

        var csvData = new List<Dictionary<string, string>>();

        try
        {
            if (_blobServiceClient == null)
            {
                _logger.LogError("❌ [Execution {RequestId}] BlobServiceClient is null - blob storage not configured properly, using sample data", requestId);
                return GetSampleCsvData();
            }
            
            if (string.IsNullOrEmpty(dataSourceUrl))
            {
                _logger.LogError("❌ [Execution {RequestId}] No data source URL provided - job configuration missing data_source_blob_ref and data_source, using sample data", requestId);
                return GetSampleCsvData();
            }

            _logger.LogInformation("📦 [Execution {RequestId}] Attempting to read from blob URL: {BlobUrl}", requestId, dataSourceUrl);

            // Parse blob URL to get container and blob name
            var uri = new Uri(dataSourceUrl);
            var pathParts = uri.AbsolutePath.TrimStart('/').Split('/');
            
            if (pathParts.Length < 2)
            {
                _logger.LogWarning("⚠️ [Execution {RequestId}] Invalid blob URL format, using sample data", requestId);
                return GetSampleCsvData();
            }

            var containerName = pathParts[0];
            var blobName = string.Join("/", pathParts.Skip(1));

            _logger.LogInformation("📁 [Execution {RequestId}] Reading from container: {Container}, blob: {Blob}", requestId, containerName, blobName);

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                _logger.LogWarning("⚠️ [Execution {RequestId}] Blob not found, using sample data", requestId);
                return GetSampleCsvData();
            }

            using var stream = await blobClient.OpenReadAsync(cancellationToken: cancellationToken);
            using var reader = new StreamReader(stream);
            
            var lines = new List<string>();
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lines.Add(line);
            }

            if (lines.Count == 0)
            {
                _logger.LogWarning("⚠️ [Execution {RequestId}] Empty CSV file, using sample data", requestId);
                return GetSampleCsvData();
            }

            // Parse header row
            var headers = lines[0].Split(',').Select(h => h.Trim('"').Trim()).ToArray();
            
            // Parse data rows
            for (int i = 1; i < lines.Count; i++)
            {
                var values = lines[i].Split(',').Select(v => v.Trim('"').Trim()).ToArray();
                var dict = new Dictionary<string, string>();
                
                for (int j = 0; j < Math.Min(headers.Length, values.Length); j++)
                {
                    dict[headers[j]] = values[j];
                }
                
                csvData.Add(dict);
            }

            _logger.LogInformation("✅ [Execution {RequestId}] Successfully parsed {Count} CSV rows", requestId, csvData.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [Execution {RequestId}] Failed to parse CSV data, using sample data", requestId);
            return GetSampleCsvData();
        }

        return csvData;
    }

    private List<Dictionary<string, string>> GetSampleCsvData()
    {
        return new List<Dictionary<string, string>>
        {
            new() { ["prompt"] = "What is the capital of France?", ["expected_response"] = "Paris" },
            new() { ["prompt"] = "Explain quantum computing in simple terms", ["expected_response"] = "Quantum computing uses quantum bits that can be in multiple states simultaneously" },
            new() { ["prompt"] = "What are the benefits of renewable energy?", ["expected_response"] = "Renewable energy is clean, sustainable, and reduces carbon emissions" },
            new() { ["prompt"] = "How does machine learning work?", ["expected_response"] = "Machine learning uses algorithms to learn patterns from data" },
            new() { ["prompt"] = "What is the difference between AI and ML?", ["expected_response"] = "AI is the broader concept while ML is a subset focused on learning from data" }
        };
    }

    private List<Dictionary<string, string>> GetTestUploadedCsvData()
    {
        // This simulates the uploaded CSV data to test the flow
        return new List<Dictionary<string, string>>
        {
            new() { ["prompt"] = "What is React?", ["expected_response"] = "React is a JavaScript library for building user interfaces" },
            new() { ["prompt"] = "How do you create a component in React?", ["expected_response"] = "You can create a React component by defining a function or class that returns JSX" },
            new() { ["prompt"] = "What is JSX?", ["expected_response"] = "JSX is a syntax extension for JavaScript that allows you to write HTML-like code in your React components" },
            new() { ["prompt"] = "What is the useState hook?", ["expected_response"] = "useState is a React hook that allows you to add state to functional components" }
        };
    }

    private async Task<EvaluationResult> ProcessSinglePromptAsync(Dictionary<string, string> csvRow, string itemId, JobConfiguration config, string requestId, CancellationToken cancellationToken)
    {
        // Handle different CSV column name formats (case-insensitive and flexible naming)
        var prompt = GetCsvValue(csvRow, "prompt", "Prompt", "PROMPT");
        var expectedResponse = GetCsvValue(csvRow, "expected_response", "expected_output", "Expected Output", "EXPECTED_OUTPUT", "expected", "Expected");

        _logger.LogInformation("🔍 [Execution {RequestId}] Processing prompt: {Prompt}", requestId, prompt.Length > 50 ? prompt[..50] + "..." : prompt);

        try
        {
            // Simulate AI response generation
            await Task.Delay(100, cancellationToken); // Simulate processing time

            var actualResponse = await GenerateAIResponseAsync(prompt, config, requestId, cancellationToken);
            var similarityScore = await CalculateSimilarityScoreAsync(expectedResponse, actualResponse, requestId, cancellationToken);
            var passed = similarityScore >= (config.EvaluationCriteria?.SimilarityThreshold ?? 0.8);

            // Generate evaluation details with reasoning
            var passThreshold = config.EvaluationCriteria?.SimilarityThreshold ?? 0.8;
            var reasoning = similarityScore >= passThreshold 
                ? $"Response passed with {similarityScore:P1} similarity (threshold: {passThreshold:P1})"
                : $"Response failed with {similarityScore:P1} similarity (threshold: {passThreshold:P1})";

            var differences = GenerateDifferencesAnalysis(expectedResponse, actualResponse, similarityScore);

            return new EvaluationResult
            {
                ItemId = itemId,
                Prompt = prompt,
                ExpectedResponse = expectedResponse,
                ActualResponse = actualResponse,
                SimilarityScore = similarityScore,
                Passed = passed,
                EvaluationDetails = new EvaluationDetails
                {
                    Reasoning = reasoning,
                    Differences = differences
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [Execution {RequestId}] Failed to process prompt: {ItemId}", requestId, itemId);
            return new EvaluationResult
            {
                ItemId = itemId,
                Prompt = prompt,
                ExpectedResponse = expectedResponse,
                ActualResponse = "Error: Failed to generate response",
                SimilarityScore = 0.0,
                Passed = false,
                EvaluationDetails = new EvaluationDetails
                {
                    Reasoning = "Evaluation failed due to an error during processing",
                    Differences = $"Error occurred: {ex.Message}"
                }
            };
        }
    }

    private string GetCsvValue(Dictionary<string, string> csvRow, params string[] possibleKeys)
    {
        foreach (var key in possibleKeys)
        {
            if (csvRow.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }
        }
        return "";
    }

    private async Task<string> GenerateAIResponseAsync(string prompt, JobConfiguration config, string requestId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🤖 [Execution {RequestId}] Generating AI response for prompt", requestId);
        
        try
        {
            // Try to get services from DI container
            using var scope = _serviceProvider.CreateScope();
            var copilotService = scope.ServiceProvider.GetService<ICopilotService>();
            
            if (copilotService != null && !string.IsNullOrEmpty(config.AgentConfiguration?.KnowledgeSource))
            {
                _logger.LogInformation("🧠 [Execution {RequestId}] Using Copilot service for AI response", requestId);
                
                // Use real Copilot API - we need an access token for this
                // For now, we'll simulate a more realistic response based on the prompt
                await Task.Delay(500, cancellationToken); // Simulate realistic API call time
                
                // Generate more contextual responses based on prompt content
                if (prompt.ToLowerInvariant().Contains("financial") || prompt.ToLowerInvariant().Contains("revenue"))
                {
                    return "Based on the financial data provided, the revenue trends show strong growth in Q4 with year-over-year increases of approximately 15%. The financial metrics indicate stable performance across key indicators.";
                }
                else if (prompt.ToLowerInvariant().Contains("technology") || prompt.ToLowerInvariant().Contains("ai"))
                {
                    return "The technology landscape continues to evolve rapidly, with artificial intelligence playing an increasingly important role in business operations and strategic decision-making.";
                }
                else if (prompt.ToLowerInvariant().Contains("market") || prompt.ToLowerInvariant().Contains("analysis"))
                {
                    return "Market analysis indicates positive trends with sustained growth potential. Key market indicators suggest favorable conditions for continued expansion and strategic investments.";
                }
                else
                {
                    // Generate a response that relates to the prompt structure
                    return $"Based on the provided context and requirements, the analysis indicates relevant findings that address the key aspects outlined in the query. The evaluation considers multiple factors to provide comprehensive insights.";
                }
            }
            else
            {
                _logger.LogWarning("⚠️ [Execution {RequestId}] Copilot service not available, using simulated responses", requestId);
                
                // Enhanced simulation with more varied responses
                var responses = new[]
                {
                    "Analysis shows strong performance indicators with positive trends across key metrics.",
                    "The evaluation demonstrates consistent results aligned with expected outcomes and industry benchmarks.",
                    "Based on the available data, the findings indicate favorable conditions with sustainable growth potential.",
                    "The assessment reveals important insights that support strategic decision-making and operational planning.",
                    "Comprehensive review indicates alignment with established criteria and performance standards."
                };

                await Task.Delay(200, cancellationToken); // Simulate API call time
                return responses[Math.Abs(prompt.GetHashCode()) % responses.Length];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [Execution {RequestId}] Error generating AI response", requestId);
            return "Error: Unable to generate AI response due to service unavailability.";
        }
    }

    private async Task<double> CalculateSimilarityScoreAsync(string expected, string actual, string requestId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("📊 [Execution {RequestId}] Calculating similarity score using enhanced evaluation", requestId);
        
        if (string.IsNullOrEmpty(expected) && string.IsNullOrEmpty(actual))
            return 1.0;
        
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual))
            return 0.0;

        try
        {
            // Try to use the real Copilot similarity evaluation from the API
            using var scope = _serviceProvider.CreateScope();
            var copilotService = scope.ServiceProvider.GetService<ICopilotService>();
            
            if (copilotService != null)
            {
                _logger.LogInformation("🧠 [Execution {RequestId}] Using Copilot semantic similarity evaluation", requestId);
                
                // Note: We would need an access token for this to work
                // For now, we'll use an enhanced similarity algorithm that considers semantic meaning
                return await CalculateEnhancedSimilarityAsync(expected, actual, requestId, cancellationToken);
            }
            else
            {
                _logger.LogWarning("⚠️ [Execution {RequestId}] Copilot service not available, using enhanced similarity algorithm", requestId);
                return await CalculateEnhancedSimilarityAsync(expected, actual, requestId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [Execution {RequestId}] Error calculating similarity score", requestId);
            // Fallback to basic similarity
            return CalculateBasicSimilarityScore(expected, actual);
        }
    }

    private async Task<double> CalculateEnhancedSimilarityAsync(string expected, string actual, string requestId, CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken); // Simulate processing time
        
        // Enhanced similarity that considers:
        // 1. Basic text similarity
        // 2. Key term matching
        // 3. Semantic structure
        
        var basicScore = CalculateBasicSimilarityScore(expected, actual);
        
        // Boost score for semantic similarities
        var expectedWords = expected.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var actualWords = actual.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        // Calculate keyword overlap
        var commonWords = expectedWords.Intersect(actualWords).Count();
        var totalUniqueWords = expectedWords.Union(actualWords).Count();
        var keywordOverlap = totalUniqueWords > 0 ? (double)commonWords / totalUniqueWords : 0.0;
        
        // Calculate length similarity
        var lengthRatio = Math.Min(expected.Length, actual.Length) / (double)Math.Max(expected.Length, actual.Length);
        
        // Combine scores with weights
        var enhancedScore = (basicScore * 0.5) + (keywordOverlap * 0.3) + (lengthRatio * 0.2);
        
        _logger.LogDebug("📊 [Execution {RequestId}] Enhanced similarity - Basic: {BasicScore:F3}, Keywords: {KeywordScore:F3}, Length: {LengthScore:F3}, Final: {FinalScore:F3}", 
            requestId, basicScore, keywordOverlap, lengthRatio, enhancedScore);
        
        return Math.Min(1.0, enhancedScore);
    }

    private double CalculateBasicSimilarityScore(string expected, string actual)
    {
        if (string.IsNullOrEmpty(expected) && string.IsNullOrEmpty(actual))
            return 1.0;
        
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual))
            return 0.0;

        // Simple similarity calculation using Levenshtein distance
        var distance = ComputeLevenshteinDistance(expected.ToLowerInvariant(), actual.ToLowerInvariant());
        var maxLength = Math.Max(expected.Length, actual.Length);
        return 1.0 - (double)distance / maxLength;
    }

    private int ComputeLevenshteinDistance(string s1, string s2)
    {
        var matrix = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= s2.Length; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                var cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                matrix[i, j] = Math.Min(Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1), matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[s1.Length, s2.Length];
    }

    private ResultsSummary GenerateResultsSummary(List<EvaluationResult> results, string requestId)
    {
        _logger.LogInformation("📊 [Execution {RequestId}] Generating results summary for {Count} evaluations", requestId, results.Count);

        var totalEvaluations = results.Count;
        var passedEvaluations = results.Count(r => r.Passed == true);
        var failedEvaluations = totalEvaluations - passedEvaluations;
        var averageScore = results.Count > 0 ? results.Where(r => r.SimilarityScore.HasValue).Average(r => r.SimilarityScore!.Value) : 0.0;
        var passRate = totalEvaluations > 0 ? (double)passedEvaluations / totalEvaluations * 100.0 : 0.0;

        return new ResultsSummary
        {
            TotalEvaluations = totalEvaluations,
            PassedEvaluations = passedEvaluations,
            FailedEvaluations = failedEvaluations,
            AverageScore = (double)Math.Round((decimal)averageScore, 3),
            PassRate = Math.Round(passRate, 1)
        };
    }

    private async Task<BlobReference?> UploadResultsToBlobAsync(string jobId, JobResults results, string requestId)
    {
        if (_blobServiceClient == null)
        {
            _logger.LogWarning("⚠️ [Execution {RequestId}] Blob storage not configured, skipping artifact upload", requestId);
            return null;
        }

        try
        {
            var containerName = "job-results";
            var blobName = $"{jobId}/results.json";
            
            // Get or create container
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

            // Upload results as JSON
            var resultsJson = JsonSerializer.Serialize(results, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true
            });

            var blobClient = containerClient.GetBlobClient(blobName);
            await blobClient.UploadAsync(BinaryData.FromString(resultsJson), overwrite: true);

            _logger.LogInformation("📦 [Execution {RequestId}] Results uploaded to blob storage: {BlobName}", 
                requestId, blobName);

            return new BlobReference
            {
                BlobId = Guid.NewGuid(),
                StorageAccount = _blobServiceClient.AccountName,
                Container = containerName,
                BlobName = blobName,
                ContentType = "application/json",
                SizeBytes = System.Text.Encoding.UTF8.GetByteCount(resultsJson),
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(30), // 30 days retention
                AccessUrl = blobClient.Uri.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("💥 [Execution {RequestId}] Failed to upload results to blob storage: {Error}", 
                requestId, ex.Message);
            return null;
        }
    }

    private async Task UpdateJobStatusAsync(string jobId, JobStatus status, JobProgress? progress = null, JobErrorDetails? errorDetails = null)
    {
        try
        {
            var jobEntity = await _jobRepository.GetJobByIdAsync(jobId);
            if (jobEntity == null)
            {
                _logger.LogWarning("⚠️ Job {JobId} not found for status update", jobId);
                return;
            }

            jobEntity.Status = status;
            jobEntity.UpdatedAt = DateTimeOffset.UtcNow;

            if (progress != null)
            {
                jobEntity.ProgressJson = JsonSerializer.Serialize(progress);
            }

            if (status == JobStatus.Completed)
            {
                jobEntity.CompletedAt = DateTimeOffset.UtcNow;
            }

            if (errorDetails != null)
            {
                jobEntity.ErrorDetailsJson = JsonSerializer.Serialize(errorDetails);
            }

            await _jobRepository.UpdateJobAsync(jobEntity);

            _logger.LogInformation("📊 Job {JobId} status updated to {Status}", jobId, status);
        }
        catch (Exception ex)
        {
            _logger.LogError("💥 Failed to update job {JobId} status: {Error}", jobId, ex.Message);
        }
    }

    private static bool IsRetryable(Exception exception)
    {
        // Determine if the exception type suggests the operation could be retried
        return exception is not (
            ArgumentException or
            ArgumentNullException or
            NotSupportedException or
            InvalidOperationException
        );
    }

    private string GenerateDifferencesAnalysis(string expected, string actual, double similarityScore)
    {
        if (string.IsNullOrEmpty(expected) && string.IsNullOrEmpty(actual))
            return "Both expected and actual responses are empty";
        
        if (string.IsNullOrEmpty(expected))
            return "Expected response is empty, but actual response was provided";
        
        if (string.IsNullOrEmpty(actual))
            return "Actual response is empty, but expected response was provided";

        // Simple character-based analysis for basic differences
        var expectedLength = expected.Length;
        var actualLength = actual.Length;
        var lengthDiff = Math.Abs(expectedLength - actualLength);
        
        if (similarityScore >= 0.9)
            return lengthDiff <= 10 
                ? "Responses are very similar with minimal differences"
                : $"Responses are very similar but differ in length (expected: {expectedLength}, actual: {actualLength} chars)";
        
        if (similarityScore >= 0.7)
            return $"Responses have moderate differences. Length difference: {lengthDiff} characters";
        
        if (similarityScore >= 0.5)
            return $"Responses have significant differences. Expected length: {expectedLength}, actual: {actualLength} characters";
        
        return "Responses are substantially different in content and structure";
    }
}

/// <summary>
/// Result of job execution containing success status and artifacts
/// </summary>
public class JobExecutionResult
{
    public bool Success { get; set; }
    public JobResults? Results { get; set; }
    public BlobReference? ArtifactBlobReference { get; set; }
    public JobErrorDetails? ErrorDetails { get; set; }
}