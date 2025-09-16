using Microsoft.AspNetCore.Mvc;
using CopilotEvalApi.Models;
using CopilotEvalApi.Services;
using CopilotEvalApi.Repositories;
using System.ComponentModel.DataAnnotations;

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

    public JobsController(
        IJobRepository jobRepository,
        IJobQueueService jobQueueService,
        ILogger<JobsController> logger)
    {
        _jobRepository = jobRepository;
        _jobQueueService = jobQueueService;
        _logger = logger;
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