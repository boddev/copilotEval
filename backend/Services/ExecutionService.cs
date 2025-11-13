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
            // Retrieve the job entity to get the access token
            var jobEntity = await _jobRepository.GetJobByIdAsync(job.Id);
            if (jobEntity == null)
            {
                throw new InvalidOperationException($"Job {job.Id} not found in repository");
            }

            // Update job status to Running
            await UpdateJobStatusAsync(job.Id, JobStatus.Running, 
                new JobProgress(TotalItems: 100, CompletedItems: 0, Percentage: 0.0));

            var result = job.Type switch
            {
                JobType.BulkEvaluation => await ExecuteBulkEvaluationAsync(job, jobEntity.AccessToken, requestId, cancellationToken),
                JobType.SingleEvaluation => await ExecuteSingleEvaluationAsync(job, jobEntity.AccessToken, requestId, cancellationToken),
                JobType.BatchProcessing => await ExecuteBatchProcessingAsync(job, jobEntity.AccessToken, requestId, cancellationToken),
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

    private async Task<JobExecutionResult> ExecuteBulkEvaluationAsync(Job job, string? accessToken, string requestId, CancellationToken cancellationToken)
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
            var evaluationResult = await ProcessSinglePromptAsync(csvRow, itemId, job.Configuration, accessToken, requestId, cancellationToken);
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

    private async Task<JobExecutionResult> ExecuteSingleEvaluationAsync(Job job, string? accessToken, string requestId, CancellationToken cancellationToken)
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

    private async Task<JobExecutionResult> ExecuteBatchProcessingAsync(Job job, string? accessToken, string requestId, CancellationToken cancellationToken)
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

            // Parse header row with proper CSV parsing
            var headers = ParseCsvLine(lines[0]);
            
            // Parse data rows with proper CSV parsing
            for (int i = 1; i < lines.Count; i++)
            {
                var values = ParseCsvLine(lines[i]);
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

    private string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var currentField = new StringBuilder();
        bool inQuotes = false;
        
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            
            if (c == '"')
            {
                // Handle escaped quotes ("")
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    currentField.Append('"');
                    i++; // Skip the next quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                // End of field
                result.Add(currentField.ToString().Trim());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }
        
        // Add the last field
        result.Add(currentField.ToString().Trim());
        
        return result.ToArray();
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

    private async Task<EvaluationResult> ProcessSinglePromptAsync(Dictionary<string, string> csvRow, string itemId, JobConfiguration config, string? accessToken, string requestId, CancellationToken cancellationToken)
    {
        // Handle different CSV column name formats (case-insensitive and flexible naming)
        var prompt = GetCsvValue(csvRow, "prompt", "Prompt", "PROMPT", "Question");
        var expectedResponse = GetCsvValue(csvRow, "expected_response", "expected_output", "Expected Output", "EXPECTED_OUTPUT", "expected", "Expected", "Answer");

        _logger.LogInformation("🔍 [Execution {RequestId}] Processing prompt: {Prompt}", requestId, prompt.Length > 50 ? prompt[..50] + "..." : prompt);

        try
        {
            // Simulate AI response generation
            await Task.Delay(100, cancellationToken); // Simulate processing time

            var actualResponse = await GenerateAIResponseAsync(prompt, config, accessToken, requestId, cancellationToken);
            var evaluationResult = await CalculateSimilarityScoreAsync(expectedResponse, actualResponse, accessToken, requestId, cancellationToken);
            var passed = evaluationResult.Score >= (config.EvaluationCriteria?.SimilarityThreshold ?? 0.8);

            // Generate evaluation details with reasoning from Copilot semantic evaluation
            var passThreshold = config.EvaluationCriteria?.SimilarityThreshold ?? 0.8;
            var statusReasoning = evaluationResult.Score >= passThreshold 
                ? $"Response passed with {evaluationResult.Score:P1} similarity (threshold: {passThreshold:P1})"
                : $"Response failed with {evaluationResult.Score:P1} similarity (threshold: {passThreshold:P1})";

            // Combine status reasoning with Copilot's semantic evaluation reasoning
            var fullReasoning = $"{statusReasoning}. {evaluationResult.Reasoning}";

            return new EvaluationResult
            {
                ItemId = itemId,
                Prompt = prompt,
                ExpectedResponse = expectedResponse,
                ActualResponse = actualResponse,
                SimilarityScore = evaluationResult.Score,
                Passed = passed,
                EvaluationDetails = new EvaluationDetails
                {
                    Reasoning = fullReasoning,
                    Differences = evaluationResult.Differences
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

    private async Task<string> GenerateAIResponseAsync(string prompt, JobConfiguration config, string? accessToken, string requestId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🤖 [Execution {RequestId}] Generating AI response for prompt: {PromptPreview}", 
            requestId, prompt.Length > 100 ? prompt[..100] + "..." : prompt);
        
        try
        {
            // Try to get Copilot service and GraphSearchService from DI container
            using var scope = _serviceProvider.CreateScope();
            var copilotService = scope.ServiceProvider.GetService<ICopilotService>();
            var graphSearchService = scope.ServiceProvider.GetService<GraphSearchService>();
            
            if (copilotService == null)
            {
                _logger.LogError("❌ [Execution {RequestId}] Copilot service not available", requestId);
                return "Error: Copilot service not configured.";
            }

            // Check if access token is available
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogError("❌ [Execution {RequestId}] No access token available. User must be authenticated to use Copilot API.", requestId);
                return "Error: No access token available for Copilot API. Please authenticate first.";
            }

            _logger.LogInformation("🧠 [Execution {RequestId}] Using Copilot Chat API for AI response", requestId);
            
            // Create a new conversation for this prompt
            string conversationId;
            try
            {
                conversationId = await copilotService.CreateConversationAsync(accessToken);
                _logger.LogInformation("✅ [Execution {RequestId}] Created conversation: {ConversationId}", requestId, conversationId);
            }
            catch (Exception convEx)
            {
                _logger.LogError(convEx, "❌ [Execution {RequestId}] Failed to create conversation", requestId);
                return $"Error: Failed to create Copilot conversation: {convEx.Message}";
            }
            
            // Prepare the message text, incorporating instructions and knowledge source directly into the prompt
            // This matches the behavior of the single prompt endpoint for consistent results
            var messageText = prompt;
            
            // If additional instructions are provided, prepend them to the main prompt
            if (!string.IsNullOrWhiteSpace(config.AgentConfiguration?.AdditionalInstructions))
            {
                messageText = $"{config.AgentConfiguration.AdditionalInstructions}\n\n{messageText}";
                _logger.LogInformation("📋 [Execution {RequestId}] Embedded instructions in prompt. Total length: {Length} chars", 
                    requestId, messageText.Length);
            }
            
            // Search knowledge source if specified and embed results in prompt
            if (!string.IsNullOrWhiteSpace(config.AgentConfiguration?.KnowledgeSource) && graphSearchService != null)
            {
                _logger.LogInformation("🔍 [Execution {RequestId}] Searching knowledge source: {ConnectionId}", 
                    requestId, config.AgentConfiguration.KnowledgeSource);
                
                try
                {
                    var searchResults = await graphSearchService.SearchKnowledgeSourceAsync(
                        accessToken, 
                        config.AgentConfiguration.KnowledgeSource, 
                        prompt, // Use original prompt for search
                        5 // Max results
                    );
                    
                    if (searchResults.Any())
                    {
                        _logger.LogInformation("📊 [Execution {RequestId}] Found {ResultCount} results from knowledge source", 
                            requestId, searchResults.Count);
                        
                        var knowledgeContext = graphSearchService.FormatSearchResultsAsContext(
                            searchResults, 
                            config.AgentConfiguration.KnowledgeSource);
                        
                        // Embed knowledge source results directly in the prompt
                        messageText = $"Based on the following information from {config.AgentConfiguration.KnowledgeSource}:\n\n{knowledgeContext}\n\n{messageText}";
                        
                        _logger.LogInformation("📚 [Execution {RequestId}] Embedded knowledge source results in prompt. Total length: {Length} chars", 
                            requestId, messageText.Length);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ [Execution {RequestId}] No results found in knowledge source: {ConnectionId}", 
                            requestId, config.AgentConfiguration.KnowledgeSource);
                        
                        // Inform that no knowledge was found
                        messageText = $"Note: No information was found in {config.AgentConfiguration.KnowledgeSource} knowledge source.\n\n{messageText}";
                    }
                }
                catch (Exception searchEx)
                {
                    _logger.LogError(searchEx, "❌ [Execution {RequestId}] Failed to search knowledge source", requestId);
                    // Continue without knowledge source results
                }
            }
            
            // Prepare the chat request with proper model structure
            // Note: Web search grounding is enabled by default unless explicitly disabled
            // DisableWebSearchGrounding can be set to true if you only want enterprise search
            var chatRequest = new CopilotChatRequest(
                Message: new CopilotConversationRequestMessage(Text: messageText),
                AdditionalContext: null, // All context now embedded in main message for consistency with single prompt endpoint
                LocationHint: new CopilotConversationLocation(
                    Latitude: null,
                    Longitude: null,
                    TimeZone: "America/New_York", // IANA timezone format required (consistent with single prompt endpoint)
                    CountryOrRegion: "US",
                    CountryOrRegionConfidence: null
                ),
                DisableWebSearchGrounding: null, // null = use default (enabled)
                FileReferences: null // Future: Support OneDrive/SharePoint file context
            );
            
            // Call Copilot Chat API
            _logger.LogInformation("📡 [Execution {RequestId}] Sending chat request to Copilot API with prompt length: {Length} chars", 
                requestId, messageText.Length);
            var response = await copilotService.ChatAsync(accessToken, conversationId, chatRequest);
            
            if (response?.Messages != null && response.Messages.Count > 0)
            {
                // Get the last message which should be Copilot's response
                var lastMessage = response.Messages[^1];
                var aiResponse = lastMessage.Text;
                
                _logger.LogInformation("✅ [Execution {RequestId}] Received Copilot response ({Length} chars)", 
                    requestId, aiResponse?.Length ?? 0);
                
                return aiResponse ?? "No response text received from Copilot.";
            }
            else
            {
                _logger.LogWarning("⚠️ [Execution {RequestId}] Copilot returned empty response", requestId);
                return "No response received from Copilot API.";
            }
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "❌ [Execution {RequestId}] HTTP error calling Copilot API", requestId);
            return $"Error: Copilot API request failed: {httpEx.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [Execution {RequestId}] Error generating AI response", requestId);
            return $"Error: Unable to generate AI response: {ex.Message}";
        }
    }

    private List<CopilotContextMessage>? BuildAdditionalContext(JobConfiguration config)
    {
        var contextMessages = new List<CopilotContextMessage>();
        
        if (!string.IsNullOrEmpty(config.AgentConfiguration?.AdditionalInstructions))
        {
            contextMessages.Add(new CopilotContextMessage(
                Text: config.AgentConfiguration.AdditionalInstructions,
                Description: "Additional instructions"
            ));
        }
        
        if (!string.IsNullOrEmpty(config.AgentConfiguration?.KnowledgeSource))
        {
            contextMessages.Add(new CopilotContextMessage(
                Text: $"Use knowledge from: {config.AgentConfiguration.KnowledgeSource}",
                Description: "Knowledge source reference"
            ));
        }
        
        return contextMessages.Count > 0 ? contextMessages : null;
    }

    private async Task<SemanticEvaluationResult> CalculateSimilarityScoreAsync(string expected, string actual, string? accessToken, string requestId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("📊 [Execution {RequestId}] Calculating similarity score using Copilot semantic evaluation", requestId);
        
        if (string.IsNullOrEmpty(expected) && string.IsNullOrEmpty(actual))
            return new SemanticEvaluationResult 
            { 
                Score = 1.0, 
                Reasoning = "Both responses are empty",
                Differences = "None"
            };
        
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual))
            return new SemanticEvaluationResult 
            { 
                Score = 0.0, 
                Reasoning = string.IsNullOrEmpty(expected) ? "Expected response is empty" : "Actual response is empty",
                Differences = "One response is missing"
            };

        try
        {
            // Use Copilot for semantic similarity evaluation (same as single prompt endpoint)
            using var scope = _serviceProvider.CreateScope();
            var copilotService = scope.ServiceProvider.GetService<ICopilotService>();
            
            if (copilotService == null)
            {
                _logger.LogWarning("⚠️ [Execution {RequestId}] Copilot service not available, using fallback algorithm", requestId);
                var fallbackScore = await CalculateEnhancedSimilarityAsync(expected, actual, requestId, cancellationToken);
                return new SemanticEvaluationResult 
                { 
                    Score = fallbackScore,
                    Reasoning = "Copilot service unavailable - used Levenshtein distance algorithm",
                    Differences = GenerateDifferencesAnalysis(expected, actual, fallbackScore)
                };
            }

            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogWarning("⚠️ [Execution {RequestId}] No access token available for Copilot evaluation, using fallback", requestId);
                var fallbackScore = await CalculateEnhancedSimilarityAsync(expected, actual, requestId, cancellationToken);
                return new SemanticEvaluationResult 
                { 
                    Score = fallbackScore,
                    Reasoning = "No access token available - used Levenshtein distance algorithm",
                    Differences = GenerateDifferencesAnalysis(expected, actual, fallbackScore)
                };
            }
            
            _logger.LogInformation("🧠 [Execution {RequestId}] Using Copilot API for semantic similarity evaluation", requestId);
            
            // Create conversation for evaluation
            var conversationId = await copilotService.CreateConversationAsync(accessToken);
            
            // Construct evaluation prompt (same as single prompt endpoint)
            var evaluationPrompt = $@"You are an expert evaluator. Please compare these two responses and determine if they provide semantically equivalent answers.

Expected Response: ""{expected}""
Actual Response: ""{actual}""

Please analyze the semantic similarity and respond with EXACTLY this format (no additional text before or after):

Score: [number between 0.0 and 1.0]
Reasoning: [brief explanation of your evaluation]
Differences: [key differences, or 'None' if semantically equivalent]

Scoring guide:
- 1.0 = Semantically identical (same meaning, even if different wording)
- 0.8-0.9 = Very similar meaning with minor differences
- 0.5-0.7 = Partially similar but notable differences in meaning
- 0.2-0.4 = Different meanings but some related concepts
- 0.0-0.1 = Completely different meanings

Focus on semantic meaning rather than exact word matching. Start your response with 'Score:'";

            var chatRequest = new CopilotChatRequest(
                Message: new CopilotConversationRequestMessage(evaluationPrompt),
                AdditionalContext: null,
                LocationHint: new CopilotConversationLocation(
                    Latitude: null,
                    Longitude: null,
                    TimeZone: "America/New_York",
                    CountryOrRegion: null,
                    CountryOrRegionConfidence: null
                )
            );

            var response = await copilotService.ChatAsync(accessToken, conversationId, chatRequest);
            var responseText = response.Messages.LastOrDefault()?.Text ?? "";
            
            // Parse the score, reasoning, and differences from Copilot's response
            var scoreMatch = System.Text.RegularExpressions.Regex.Match(responseText, @"Score:\s*([\d.]+)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var reasoningMatch = System.Text.RegularExpressions.Regex.Match(responseText, @"Reasoning:\s*(.+?)(?=\n(?:Differences:|$))", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            var differencesMatch = System.Text.RegularExpressions.Regex.Match(responseText, @"Differences:\s*(.+?)$", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            
            if (scoreMatch.Success && double.TryParse(scoreMatch.Groups[1].Value, out var score))
            {
                var reasoning = reasoningMatch.Success ? reasoningMatch.Groups[1].Value.Trim() : "Semantic evaluation completed";
                var differences = differencesMatch.Success ? differencesMatch.Groups[1].Value.Trim() : "See reasoning for details";
                
                _logger.LogInformation("✅ [Execution {RequestId}] Copilot evaluated similarity: {Score:F3}", requestId, score);
                
                return new SemanticEvaluationResult 
                { 
                    Score = Math.Clamp(score, 0.0, 1.0),
                    Reasoning = reasoning,
                    Differences = differences
                };
            }
            else
            {
                _logger.LogWarning("⚠️ [Execution {RequestId}] Could not parse Copilot score response, using fallback", requestId);
                var fallbackScore = await CalculateEnhancedSimilarityAsync(expected, actual, requestId, cancellationToken);
                return new SemanticEvaluationResult 
                { 
                    Score = fallbackScore,
                    Reasoning = "Failed to parse Copilot response - used fallback algorithm",
                    Differences = GenerateDifferencesAnalysis(expected, actual, fallbackScore)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [Execution {RequestId}] Error calculating similarity with Copilot, using fallback", requestId);
            var fallbackScore = await CalculateEnhancedSimilarityAsync(expected, actual, requestId, cancellationToken);
            return new SemanticEvaluationResult 
            { 
                Score = fallbackScore,
                Reasoning = $"Error during Copilot evaluation: {ex.Message}",
                Differences = GenerateDifferencesAnalysis(expected, actual, fallbackScore)
            };
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
/// Contains detailed semantic evaluation results from Copilot
/// </summary>
internal class SemanticEvaluationResult
{
    public double Score { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public string Differences { get; set; } = string.Empty;
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