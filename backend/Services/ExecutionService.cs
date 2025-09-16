using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CopilotEvalApi.Models;
using CopilotEvalApi.Repositories;

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

    public ExecutionService(
        ILogger<ExecutionService> logger,
        IConfiguration configuration,
        IJobRepository jobRepository)
    {
        _logger = logger;
        _configuration = configuration;
        _jobRepository = jobRepository;

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

        // Simulate bulk evaluation processing with progress updates
        var totalItems = 50; // Simulated number of evaluation items
        var results = new List<EvaluationResult>();

        for (int i = 1; i <= totalItems; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Simulate evaluation processing time
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);

            // Generate mock evaluation result
            var evaluationResult = new EvaluationResult
            {
                ItemId = $"item_{i:D3}",
                Prompt = $"Test prompt {i}",
                ExpectedResponse = $"Expected response for item {i}",
                ActualResponse = $"Actual response for item {i}",
                SimilarityScore = Random.Shared.NextDouble() * 0.4 + 0.6, // Score between 0.6 and 1.0
                Passed = Random.Shared.NextDouble() > 0.2, // 80% pass rate
                EvaluationDetails = new EvaluationDetails
                {
                    Reasoning = $"Evaluation reasoning for item {i}",
                    Differences = Random.Shared.NextDouble() > 0.8 ? "Minor wording differences" : "None"
                }
            };

            results.Add(evaluationResult);

            // Update progress every 10 items
            if (i % 10 == 0)
            {
                var progress = new JobProgress(totalItems, i, (double)i / totalItems * 100);
                await UpdateJobStatusAsync(job.Id, JobStatus.Running, progress);
                _logger.LogInformation("📈 [Execution {RequestId}] Progress update: {Completed}/{Total} ({Percentage:F1}%)", 
                    requestId, i, totalItems, progress.Percentage);
            }
        }

        // Generate summary
        var summary = new ResultsSummary
        {
            TotalEvaluations = results.Count,
            PassedEvaluations = results.Count(r => r.Passed == true),
            FailedEvaluations = results.Count(r => r.Passed == false),
            AverageScore = results.Average(r => r.SimilarityScore ?? 0),
            PassRate = (double)results.Count(r => r.Passed == true) / results.Count * 100
        };

        // Create job results
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