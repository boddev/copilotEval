using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotEvalApi.Models;

/// <summary>
/// Entity representing a job in the database
/// </summary>
public class JobEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(1000)]
    public string? Description { get; set; }
    
    [Required]
    public JobType Type { get; set; }
    
    [Required]
    public JobStatus Status { get; set; }
    
    [Required]
    public DateTimeOffset CreatedAt { get; set; }
    
    [Required]
    public DateTimeOffset UpdatedAt { get; set; }
    
    public DateTimeOffset? CompletedAt { get; set; }
    
    public DateTimeOffset? EstimatedCompletion { get; set; }
    
    // Store configuration as JSON string
    [Required]
    public string ConfigurationJson { get; set; } = string.Empty;
    
    // Store progress as JSON string
    [Required]
    public string ProgressJson { get; set; } = string.Empty;
    
    // Store error details as JSON string (nullable)
    public string? ErrorDetailsJson { get; set; }

    // Store results blob reference as JSON string (nullable)
    public string? ResultsBlobReferenceJson { get; set; }

    /// <summary>
    /// Convert JobEntity to Job model
    /// </summary>
    public Job ToJob()
    {
        var configuration = JsonSerializer.Deserialize<JobConfiguration>(ConfigurationJson) 
                           ?? new JobConfiguration();
        var progress = JsonSerializer.Deserialize<JobProgress>(ProgressJson) 
                      ?? new JobProgress(0, 0, 0.0);
        
        JobErrorDetails? errorDetails = null;
        if (!string.IsNullOrEmpty(ErrorDetailsJson))
        {
            errorDetails = JsonSerializer.Deserialize<JobErrorDetails>(ErrorDetailsJson);
        }

        return new Job(
            Id: Id,
            Name: Name,
            Type: Type,
            Status: Status,
            CreatedAt: CreatedAt,
            UpdatedAt: UpdatedAt,
            Progress: progress,
            Configuration: configuration
        )
        {
            Description = Description,
            CompletedAt = CompletedAt,
            EstimatedCompletion = EstimatedCompletion,
            ErrorDetails = errorDetails
        };
    }

    /// <summary>
    /// Create JobEntity from JobCreateRequest
    /// </summary>
    public static JobEntity FromCreateRequest(JobCreateRequest request, string jobId)
    {
        var now = DateTimeOffset.UtcNow;
        var initialProgress = new JobProgress(0, 0, 0.0);
        
        return new JobEntity
        {
            Id = jobId,
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            Status = JobStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationJson = JsonSerializer.Serialize(request.Configuration),
            ProgressJson = JsonSerializer.Serialize(initialProgress)
        };
    }
}