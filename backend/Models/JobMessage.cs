using System.Text.Json.Serialization;

namespace CopilotEvalApi.Models;

/// <summary>
/// Represents a message in the job processing queue for asynchronous job handling
/// </summary>
public record JobMessage(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("message_type")] JobMessageType MessageType,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("payload")] object Payload
)
{
    [JsonPropertyName("correlation_id")]
    public Guid? CorrelationId { get; init; }

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; init; } = 0;

    [JsonPropertyName("blob_references")]
    public List<BlobReference>? BlobReferences { get; init; }
}

/// <summary>
/// Types of job messages for queue processing
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JobMessageType
{
    [JsonPropertyName("job_created")]
    JobCreated,
    
    [JsonPropertyName("job_started")]
    JobStarted,
    
    [JsonPropertyName("job_progress")]
    JobProgress,
    
    [JsonPropertyName("job_completed")]
    JobCompleted,
    
    [JsonPropertyName("job_failed")]
    JobFailed,
    
    [JsonPropertyName("job_cancelled")]
    JobCancelled
}

/// <summary>
/// Job-related models for API and queue processing
/// </summary>
public record JobCreateRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] JobType Type,
    [property: JsonPropertyName("configuration")] JobConfiguration Configuration
)
{
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public record Job(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] JobType Type,
    [property: JsonPropertyName("status")] JobStatus Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("progress")] JobProgress Progress,
    [property: JsonPropertyName("configuration")] JobConfiguration Configuration
)
{
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("estimated_completion")]
    public DateTimeOffset? EstimatedCompletion { get; init; }

    [JsonPropertyName("error_details")]
    public JobErrorDetails? ErrorDetails { get; init; }
}

public record JobDetails : Job
{
    [JsonPropertyName("results")]
    public JobResults? Results { get; init; }

    public JobDetails(Job job, JobResults? results = null) 
        : base(job.Id, job.Name, job.Type, job.Status, job.CreatedAt, job.UpdatedAt, job.Progress, job.Configuration)
    {
        Description = job.Description;
        CompletedAt = job.CompletedAt;
        EstimatedCompletion = job.EstimatedCompletion;
        ErrorDetails = job.ErrorDetails;
        Results = results;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JobType
{
    [JsonPropertyName("bulk_evaluation")]
    BulkEvaluation,
    
    [JsonPropertyName("single_evaluation")]
    SingleEvaluation,
    
    [JsonPropertyName("batch_processing")]
    BatchProcessing
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JobStatus
{
    [JsonPropertyName("pending")]
    Pending,
    
    [JsonPropertyName("running")]
    Running,
    
    [JsonPropertyName("completed")]
    Completed,
    
    [JsonPropertyName("failed")]
    Failed,
    
    [JsonPropertyName("cancelled")]
    Cancelled
}

public record JobConfiguration
{
    [JsonPropertyName("data_source")]
    public string? DataSource { get; init; }

    [JsonPropertyName("data_source_blob_ref")]
    public string? DataSourceBlobRef { get; init; }

    [JsonPropertyName("prompt_template")]
    public string? PromptTemplate { get; init; }

    [JsonPropertyName("evaluation_criteria")]
    public EvaluationCriteria? EvaluationCriteria { get; init; }

    [JsonPropertyName("agent_configuration")]
    public AgentConfiguration? AgentConfiguration { get; init; }
}

public record EvaluationCriteria
{
    [JsonPropertyName("similarity_threshold")]
    public double? SimilarityThreshold { get; init; }

    [JsonPropertyName("use_semantic_scoring")]
    public bool UseSemanticsScoring { get; init; } = true;

    [JsonPropertyName("custom_evaluators")]
    public List<string>? CustomEvaluators { get; init; }
}

public record AgentConfiguration
{
    [JsonPropertyName("selected_agent_id")]
    public string? SelectedAgentId { get; init; }

    [JsonPropertyName("additional_instructions")]
    public string? AdditionalInstructions { get; init; }

    [JsonPropertyName("knowledge_source")]
    public string? KnowledgeSource { get; init; }
}

public record JobProgress(
    [property: JsonPropertyName("total_items")] int TotalItems,
    [property: JsonPropertyName("completed_items")] int CompletedItems,
    [property: JsonPropertyName("percentage")] double Percentage
);

public record JobErrorDetails
{
    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("error_timestamp")]
    public DateTimeOffset? ErrorTimestamp { get; init; }

    [JsonPropertyName("retry_possible")]
    public bool? RetryPossible { get; init; }
}

public record JobResults
{
    [JsonPropertyName("summary")]
    public ResultsSummary? Summary { get; init; }

    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; init; }

    [JsonPropertyName("detailed_results")]
    public List<EvaluationResult>? DetailedResults { get; init; }
}

public record ResultsSummary
{
    [JsonPropertyName("total_evaluations")]
    public int? TotalEvaluations { get; init; }

    [JsonPropertyName("passed_evaluations")]
    public int? PassedEvaluations { get; init; }

    [JsonPropertyName("failed_evaluations")]
    public int? FailedEvaluations { get; init; }

    [JsonPropertyName("average_score")]
    public double? AverageScore { get; init; }

    [JsonPropertyName("pass_rate")]
    public double? PassRate { get; init; }
}

public record EvaluationResult
{
    [JsonPropertyName("item_id")]
    public string? ItemId { get; init; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    [JsonPropertyName("expected_response")]
    public string? ExpectedResponse { get; init; }

    [JsonPropertyName("actual_response")]
    public string? ActualResponse { get; init; }

    [JsonPropertyName("similarity_score")]
    public double? SimilarityScore { get; init; }

    [JsonPropertyName("passed")]
    public bool? Passed { get; init; }

    [JsonPropertyName("evaluation_details")]
    public EvaluationDetails? EvaluationDetails { get; init; }
}

public record EvaluationDetails
{
    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; init; }

    [JsonPropertyName("differences")]
    public string? Differences { get; init; }
}

public record JobListResponse(
    [property: JsonPropertyName("jobs")] List<Job> Jobs,
    [property: JsonPropertyName("pagination")] PaginationInfo Pagination
);

public record PaginationInfo(
    [property: JsonPropertyName("current_page")] int CurrentPage,
    [property: JsonPropertyName("total_pages")] int TotalPages,
    [property: JsonPropertyName("total_items")] int TotalItems,
    [property: JsonPropertyName("items_per_page")] int ItemsPerPage,
    [property: JsonPropertyName("has_next")] bool HasNext,
    [property: JsonPropertyName("has_previous")] bool HasPrevious
);

/// <summary>
/// Reference to large payload data stored in blob storage
/// </summary>
public record BlobReference
{
    [JsonPropertyName("blob_id")]
    public Guid BlobId { get; init; }

    [JsonPropertyName("storage_account")]
    public string StorageAccount { get; init; } = string.Empty;

    [JsonPropertyName("container")]
    public string Container { get; init; } = string.Empty;

    [JsonPropertyName("blob_name")]
    public string BlobName { get; init; } = string.Empty;

    [JsonPropertyName("content_type")]
    public string ContentType { get; init; } = "application/json";

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; init; }

    [JsonPropertyName("access_url")]
    public string? AccessUrl { get; init; }
}

/// <summary>
/// Payload types for different job message types
/// </summary>
public record JobCreatedPayload
{
    [JsonPropertyName("job")]
    public Job? Job { get; init; }

    [JsonPropertyName("priority")]
    public JobPriority Priority { get; init; } = JobPriority.Normal;
}

public record JobProgressPayload
{
    [JsonPropertyName("progress")]
    public JobProgress? Progress { get; init; }

    [JsonPropertyName("current_item")]
    public string? CurrentItem { get; init; }

    [JsonPropertyName("estimated_time_remaining")]
    public string? EstimatedTimeRemaining { get; init; }
}

public record JobCompletedPayload
{
    [JsonPropertyName("results_summary")]
    public ResultsSummary? ResultsSummary { get; init; }

    [JsonPropertyName("results_blob_ref")]
    public BlobReference? ResultsBlobRef { get; init; }
}

public record JobFailedPayload
{
    [JsonPropertyName("error_details")]
    public JobErrorDetails? ErrorDetails { get; init; }

    [JsonPropertyName("partial_results")]
    public BlobReference? PartialResults { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JobPriority
{
    [JsonPropertyName("low")]
    Low,
    
    [JsonPropertyName("normal")]
    Normal,
    
    [JsonPropertyName("high")]
    High,
    
    [JsonPropertyName("urgent")]
    Urgent
}

/// <summary>
/// Error response format for API endpoints
/// </summary>
public record ErrorResponse(
    [property: JsonPropertyName("error")] ErrorDetails Error
);

public record ErrorDetails(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message
)
{
    [JsonPropertyName("details")]
    public Dictionary<string, object>? Details { get; init; }
}