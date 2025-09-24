using Microsoft.AspNetCore.Mvc;
using CopilotEvalApi.Models;
using CopilotEvalApi.Services;
using CopilotEvalApi.Repositories;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Azure.Storage.Blobs;

namespace CopilotEvalApi.Controllers;

/// <summary>
/// Controller for job lifecycle management
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly IJobRepository _jobRepository;
    private readonly IJobQueueService _jobQueueService;
    private readonly ILogger<JobsController> _logger;
    private readonly IConfiguration _configuration;

    public JobsController(
        IJobRepository jobRepository,
        IJobQueueService jobQueueService,
        ILogger<JobsController> logger,
        IConfiguration configuration)
    {
        _jobRepository = jobRepository;
        _jobQueueService = jobQueueService;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Submit a new evaluation job for processing
    /// </summary>
    /// <param name="request">Job creation request</param>
    /// <returns>202 Accepted with job ID and status URL</returns>
    [HttpPost]
    [ProducesResponseType(typeof(JobSubmissionResponse), 202)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> CreateJob([FromBody] JobCreateRequest request)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("🚀 [Jobs {RequestId}] Creating new job - Name: {JobName}, Type: {JobType}", 
            requestId, request.Name, request.Type);

        try
        {
            // Validate request
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("❌ [Jobs {RequestId}] Invalid request model", requestId);
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();
                
                return BadRequest(new ErrorResponse(
                    new ErrorDetails("VALIDATION_ERROR", "Request validation failed")
                    {
                        Details = new Dictionary<string, object> { { "errors", errors } }
                    }
                ));
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new ErrorResponse(
                    new ErrorDetails("VALIDATION_ERROR", "Job name is required")
                ));
            }

            if (request.Configuration == null)
            {
                return BadRequest(new ErrorResponse(
                    new ErrorDetails("VALIDATION_ERROR", "Job configuration is required")
                ));
            }

            // Generate job ID
            var jobId = GenerateJobId();
            _logger.LogInformation("🆔 [Jobs {RequestId}] Generated job ID: {JobId}", requestId, jobId);

            // Create job entity
            var jobEntity = JobEntity.FromCreateRequest(request, jobId);
            
            // Set status to Queued as per acceptance criteria
            jobEntity.Status = JobStatus.Pending; // Note: Using Pending as it's equivalent to Queued in the enum
            
            _logger.LogInformation("💾 [Jobs {RequestId}] Saving job to database", requestId);
            
            // Save to database
            await _jobRepository.CreateJobAsync(jobEntity);
            
            _logger.LogInformation("✅ [Jobs {RequestId}] Job saved to database with status: {Status}", 
                requestId, jobEntity.Status);

            // Convert to Job model for queue message
            var job = jobEntity.ToJob();
            
            _logger.LogInformation("📤 [Jobs {RequestId}] Enqueuing job message to Service Bus", requestId);
            
            // Enqueue to Service Bus
            await _jobQueueService.EnqueueJobCreatedAsync(jobId, job);
            
            _logger.LogInformation("✅ [Jobs {RequestId}] Job message enqueued successfully", requestId);

            // Prepare response
            var statusUrl = $"{Request.Scheme}://{Request.Host}/api/jobs/{jobId}";
            var response = new JobSubmissionResponse(jobId, statusUrl);

            _logger.LogInformation("🎉 [Jobs {RequestId}] Job creation completed successfully - JobId: {JobId}, StatusUrl: {StatusUrl}", 
                requestId, jobId, statusUrl);

            return Accepted(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("💥 [Jobs {RequestId}] Error creating job: {Error}", requestId, ex.Message);
            _logger.LogError("🔍 [Jobs {RequestId}] Exception Details: {Details}", requestId, ex.ToString());

            return StatusCode(500, new ErrorResponse(
                new ErrorDetails("INTERNAL_ERROR", "An error occurred while creating the job")
                {
                    Details = new Dictionary<string, object> { { "trace_id", requestId } }
                }
            ));
        }
    }

    /// <summary>
    /// Get details of a specific job
    /// </summary>
    /// <param name="id">Job ID</param>
    /// <returns>Job details</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Job), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public async Task<IActionResult> GetJob(string id)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("🔍 [Jobs {RequestId}] Retrieving job: {JobId}", requestId, id);

        try
        {
            var jobEntity = await _jobRepository.GetJobByIdAsync(id);
            
            if (jobEntity == null)
            {
                _logger.LogWarning("❌ [Jobs {RequestId}] Job not found: {JobId}", requestId, id);
                return NotFound(new ErrorResponse(
                    new ErrorDetails("JOB_NOT_FOUND", $"Job with ID '{id}' was not found")
                ));
            }

            var job = jobEntity.ToJob();
            
            // Add status URL to job response if needed
            var statusUrl = $"{Request.Scheme}://{Request.Host}/api/jobs/{id}";
            _logger.LogDebug("🔗 [Jobs {RequestId}] Status URL: {StatusUrl}", requestId, statusUrl);
            
            _logger.LogInformation("✅ [Jobs {RequestId}] Job retrieved successfully: {JobId}", requestId, id);
            
            return Ok(job);
        }
        catch (Exception ex)
        {
            _logger.LogError("💥 [Jobs {RequestId}] Error retrieving job {JobId}: {Error}", requestId, id, ex.Message);
            
            return StatusCode(500, new ErrorResponse(
                new ErrorDetails("INTERNAL_ERROR", "An error occurred while retrieving the job")
                {
                    Details = new Dictionary<string, object> { { "trace_id", requestId } }
                }
            ));
        }
    }

    /// <summary>
    /// Download job results as a file
    /// </summary>
    /// <param name="id">Job ID</param>
    /// <returns>Job results file</returns>
    [HttpGet("{id}/results")]
    [ProducesResponseType(typeof(FileContentResult), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> DownloadJobResults(string id)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("📥 [Download {RequestId}] Downloading results for job: {JobId}", requestId, id);

        try
        {
            // Get job details
            var jobEntity = await _jobRepository.GetJobByIdAsync(id);
            
            if (jobEntity == null)
            {
                _logger.LogWarning("❌ [Download {RequestId}] Job not found: {JobId}", requestId, id);
                return NotFound(new ErrorResponse(
                    new ErrorDetails("JOB_NOT_FOUND", $"Job with ID '{id}' was not found")
                ));
            }

            // Check if job is completed
            if (jobEntity.Status != JobStatus.Completed)
            {
                _logger.LogWarning("❌ [Download {RequestId}] Job not completed: {JobId}, Status: {Status}", requestId, id, jobEntity.Status);
                return BadRequest(new ErrorResponse(
                    new ErrorDetails("JOB_NOT_COMPLETED", $"Job '{id}' is not completed. Current status: {jobEntity.Status}")
                ));
            }

            // Check if job has results - look for stored blob reference
            if (string.IsNullOrEmpty(jobEntity.ResultsBlobReferenceJson))
            {
                _logger.LogWarning("❌ [Download {RequestId}] No results blob reference for job: {JobId}", requestId, id);
                
                // For older jobs without blob references, return basic job info
                var basicJob = jobEntity.ToJob();
                var basicDownloadData = new
                {
                    job_id = basicJob.Id,
                    job_name = basicJob.Name,
                    job_type = basicJob.Type,
                    status = basicJob.Status,
                    created_at = basicJob.CreatedAt,
                    completed_at = basicJob.CompletedAt,
                    configuration = basicJob.Configuration,
                    results = new
                    {
                        message = "Job completed successfully",
                        note = "This job was completed before detailed results storage was implemented"
                    }
                };

                var basicJobJson = JsonSerializer.Serialize(basicDownloadData, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                var basicFileName = $"job_{id}_basic_results.json";
                var basicFileBytes = System.Text.Encoding.UTF8.GetBytes(basicJobJson);

                _logger.LogInformation("✅ [Download {RequestId}] Basic results downloaded: {JobId}, Size: {Size} bytes", requestId, id, basicFileBytes.Length);
                return File(basicFileBytes, "application/json", basicFileName);
            }

            // Try to deserialize blob reference
            BlobReference? blobReference;
            try
            {
                blobReference = JsonSerializer.Deserialize<BlobReference>(jobEntity.ResultsBlobReferenceJson);
            }
            catch (Exception ex)
            {
                _logger.LogError("💥 [Download {RequestId}] Error deserializing blob reference for job {JobId}: {Error}", requestId, id, ex.Message);
                return StatusCode(500, new ErrorResponse(
                    new ErrorDetails("BLOB_REFERENCE_ERROR", "Error accessing job results")
                    {
                        Details = new Dictionary<string, object> { { "trace_id", requestId } }
                    }
                ));
            }

            if (blobReference == null)
            {
                _logger.LogWarning("❌ [Download {RequestId}] Null blob reference for job: {JobId}", requestId, id);
                return NotFound(new ErrorResponse(
                    new ErrorDetails("NO_RESULTS", $"No results available for job '{id}'")
                ));
            }

            // Try to download the actual blob content from Azure Storage
            _logger.LogInformation("📦 [Download {RequestId}] Downloading blob content: {BlobName}", requestId, blobReference.BlobName);
            
            try
            {
                // Initialize blob service client
                var storageConnectionString = _configuration.GetConnectionString("BlobStorage");
                if (string.IsNullOrEmpty(storageConnectionString) || storageConnectionString == "InMemory")
                {
                    _logger.LogWarning("⚠️ [Download {RequestId}] Blob storage not configured, returning metadata", requestId);
                    // Fallback to metadata response
                    var job = jobEntity.ToJob();
                    var downloadData = new
                    {
                        job_id = job.Id,
                        job_name = job.Name,
                        job_type = job.Type,
                        status = job.Status,
                        created_at = job.CreatedAt,
                        completed_at = job.CompletedAt,
                        configuration = job.Configuration,
                        blob_info = new
                        {
                            blob_id = blobReference.BlobId,
                            container = blobReference.Container,
                            blob_name = blobReference.BlobName,
                            content_type = blobReference.ContentType,
                            size_bytes = blobReference.SizeBytes,
                            access_url = blobReference.AccessUrl,
                            created_at = blobReference.CreatedAt
                        },
                        results = new
                        {
                            message = "Job completed with detailed results",
                            note = "Blob storage not configured for direct download. Please check blob storage settings."
                        }
                    };
                    
                    var fallbackJson = JsonSerializer.Serialize(downloadData, new JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                    });
                    var fallbackBytes = System.Text.Encoding.UTF8.GetBytes(fallbackJson);
                    var fallbackFileName = $"job_{id}_metadata.json";
                    return File(fallbackBytes, "application/json", fallbackFileName);
                }

                var blobServiceClient = new BlobServiceClient(storageConnectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient(blobReference.Container);
                var blobClient = containerClient.GetBlobClient(blobReference.BlobName);

                // Check if blob exists
                var exists = await blobClient.ExistsAsync();
                if (!exists.Value)
                {
                    _logger.LogWarning("❌ [Download {RequestId}] Blob not found: {BlobName}", requestId, blobReference.BlobName);
                    return NotFound(new ErrorResponse(
                        new ErrorDetails("BLOB_NOT_FOUND", $"Results file not found for job '{id}'")
                    ));
                }

                // Download blob content
                var blobDownloadInfo = await blobClient.DownloadAsync();
                using var memoryStream = new MemoryStream();
                await blobDownloadInfo.Value.Content.CopyToAsync(memoryStream);
                var blobBytes = memoryStream.ToArray();

                _logger.LogInformation("✅ [Download {RequestId}] Blob downloaded successfully: {BlobName}, Size: {Size} bytes", 
                    requestId, blobReference.BlobName, blobBytes.Length);

                // Try to parse the blob content as JSON to verify it contains evaluation results
                try
                {
                    var blobContent = System.Text.Encoding.UTF8.GetString(blobBytes);
                    var parsedResults = JsonSerializer.Deserialize<JsonElement>(blobContent);
                    
                    // Check if it looks like evaluation results
                    if (parsedResults.TryGetProperty("detailed_results", out var detailedResults))
                    {
                        _logger.LogInformation("📊 [Download {RequestId}] Confirmed evaluation results with {Count} detailed items", 
                            requestId, detailedResults.GetArrayLength());
                    }
                }
                catch (Exception parseEx)
                {
                    _logger.LogWarning("⚠️ [Download {RequestId}] Could not parse blob content as JSON: {Error}", requestId, parseEx.Message);
                }

                // Return the actual blob content
                var fileName = $"job_{id}_detailed_results.json";
                return File(blobBytes, blobReference.ContentType ?? "application/json", fileName);
            }
            catch (Exception blobEx)
            {
                _logger.LogError("💥 [Download {RequestId}] Error downloading blob {BlobName}: {Error}", 
                    requestId, blobReference.BlobName, blobEx.Message);
                return StatusCode(500, new ErrorResponse(
                    new ErrorDetails("BLOB_DOWNLOAD_ERROR", "Error downloading job results from storage")
                    {
                        Details = new Dictionary<string, object> 
                        { 
                            { "trace_id", requestId },
                            { "blob_name", blobReference.BlobName }
                        }
                    }
                ));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("💥 [Download {RequestId}] Error downloading job results {JobId}: {Error}", requestId, id, ex.Message);
            
            return StatusCode(500, new ErrorResponse(
                new ErrorDetails("INTERNAL_ERROR", "An error occurred while downloading the job results")
                {
                    Details = new Dictionary<string, object> { { "trace_id", requestId } }
                }
            ));
        }
    }

    /// <summary>
    /// Get list of jobs with optional filtering and pagination
    /// </summary>
    /// <param name="status">Filter by job status (pending, running, completed, failed, cancelled)</param>
    /// <param name="type">Filter by job type (bulk_evaluation, single_evaluation, batch_processing)</param>
    /// <param name="page">Page number for pagination (default: 1, min: 1)</param>
    /// <param name="limit">Items per page (default: 20, min: 1, max: 100)</param>
    /// <param name="sort">Sort field (created_at, updated_at, name)</param>
    /// <param name="order">Sort order (asc, desc, default: desc)</param>
    /// <returns>List of jobs with pagination info</returns>
    [HttpGet]
    [ProducesResponseType(typeof(JobListResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    public async Task<IActionResult> GetJobs(
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? sort = null,
        [FromQuery] string? order = null)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("🔍 [Jobs {RequestId}] Retrieving jobs list - Page: {Page}, Limit: {Limit}, Status: {Status}, Type: {Type}", 
            requestId, page, limit, status, type);

        try
        {
            // Validate parameters
            if (page < 1)
            {
                return BadRequest(new ErrorResponse(
                    new ErrorDetails("VALIDATION_ERROR", "Page number must be greater than 0")
                ));
            }

            if (limit < 1 || limit > 100)
            {
                return BadRequest(new ErrorResponse(
                    new ErrorDetails("VALIDATION_ERROR", "Limit must be between 1 and 100")
                ));
            }

            // Parse enum filters
            JobStatus? statusFilter = null;
            if (!string.IsNullOrEmpty(status))
            {
                statusFilter = status.ToLowerInvariant() switch
                {
                    "pending" => JobStatus.Pending,
                    "running" => JobStatus.Running,
                    "completed" => JobStatus.Completed,
                    "failed" => JobStatus.Failed,
                    "cancelled" => JobStatus.Cancelled,
                    _ => null
                };

                if (statusFilter == null)
                {
                    return BadRequest(new ErrorResponse(
                        new ErrorDetails("VALIDATION_ERROR", $"Invalid status value: {status}. Valid values are: pending, running, completed, failed, cancelled")
                    ));
                }
            }

            JobType? typeFilter = null;
            if (!string.IsNullOrEmpty(type))
            {
                typeFilter = type.ToLowerInvariant() switch
                {
                    "bulk_evaluation" => JobType.BulkEvaluation,
                    "single_evaluation" => JobType.SingleEvaluation,
                    "batch_processing" => JobType.BatchProcessing,
                    _ => null
                };

                if (typeFilter == null)
                {
                    return BadRequest(new ErrorResponse(
                        new ErrorDetails("VALIDATION_ERROR", $"Invalid type value: {type}. Valid values are: bulk_evaluation, single_evaluation, batch_processing")
                    ));
                }
            }

            // Validate sort and order parameters
            var validSortFields = new[] { "created_at", "updated_at", "name" };
            if (!string.IsNullOrEmpty(sort) && !validSortFields.Contains(sort.ToLowerInvariant()))
            {
                return BadRequest(new ErrorResponse(
                    new ErrorDetails("VALIDATION_ERROR", $"Invalid sort field: {sort}. Valid values are: {string.Join(", ", validSortFields)}")
                ));
            }

            var validOrderValues = new[] { "asc", "desc" };
            if (!string.IsNullOrEmpty(order) && !validOrderValues.Contains(order.ToLowerInvariant()))
            {
                return BadRequest(new ErrorResponse(
                    new ErrorDetails("VALIDATION_ERROR", $"Invalid order value: {order}. Valid values are: asc, desc")
                ));
            }

            // Get jobs from repository
            var (jobEntities, totalCount) = await _jobRepository.GetJobsAsync(
                page: page,
                pageSize: limit,
                status: statusFilter,
                type: typeFilter,
                sort: sort,
                order: order
            );

            // Convert to Job models
            var jobs = jobEntities.Select(entity => entity.ToJob()).ToList();

            // Calculate pagination info
            var totalPages = (int)Math.Ceiling((double)totalCount / limit);
            var hasNext = page < totalPages;
            var hasPrevious = page > 1;

            var paginationInfo = new PaginationInfo(
                CurrentPage: page,
                TotalPages: totalPages,
                TotalItems: totalCount,
                ItemsPerPage: limit,
                HasNext: hasNext,
                HasPrevious: hasPrevious
            );

            var response = new JobListResponse(jobs, paginationInfo);

            _logger.LogInformation("✅ [Jobs {RequestId}] Jobs list retrieved successfully - {JobCount} jobs, page {Page}/{TotalPages}", 
                requestId, jobs.Count, page, totalPages);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("💥 [Jobs {RequestId}] Error retrieving jobs list: {Error}", requestId, ex.Message);
            
            return StatusCode(500, new ErrorResponse(
                new ErrorDetails("INTERNAL_ERROR", "An error occurred while retrieving the jobs list")
                {
                    Details = new Dictionary<string, object> { { "trace_id", requestId } }
                }
            ));
        }
    }

    /// <summary>
    /// Generate a unique job ID
    /// </summary>
    private static string GenerateJobId()
    {
        // Generate ID in format: job_<12 character alphanumeric>
        var chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        var result = new string(Enumerable.Repeat(chars, 12)
            .Select(s => s[random.Next(s.Length)]).ToArray());
        
        return $"job_{result}";
    }
}

/// <summary>
/// Response model for job submission
/// </summary>
public record JobSubmissionResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("job_id")] string JobId,
    [property: System.Text.Json.Serialization.JsonPropertyName("status_url")] string StatusUrl
);