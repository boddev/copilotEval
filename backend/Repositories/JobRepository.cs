using Microsoft.EntityFrameworkCore;
using CopilotEvalApi.Models;

namespace CopilotEvalApi.Repositories;

/// <summary>
/// Repository interface for job operations
/// </summary>
public interface IJobRepository
{
    Task<JobEntity> CreateJobAsync(JobEntity job);
    Task<JobEntity?> GetJobByIdAsync(string jobId);
    Task<JobEntity> UpdateJobAsync(JobEntity job);
    Task<List<JobEntity>> GetJobsAsync(int page = 1, int pageSize = 20);
    Task<(List<JobEntity> jobs, int totalCount)> GetJobsAsync(
        int page = 1, 
        int pageSize = 20, 
        string? userId = null, 
        JobStatus? status = null, 
        JobType? type = null, 
        string? sort = null, 
        string? order = null);
}

/// <summary>
/// Repository implementation for job operations
/// </summary>
public class JobRepository : IJobRepository
{
    private readonly JobDbContext _context;
    private readonly ILogger<JobRepository> _logger;

    public JobRepository(JobDbContext context, ILogger<JobRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<JobEntity> CreateJobAsync(JobEntity job)
    {
        _logger.LogInformation("📝 Creating job with ID: {JobId}", job.Id);
        
        _context.Jobs.Add(job);
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("✅ Successfully created job with ID: {JobId}", job.Id);
        return job;
    }

    public async Task<JobEntity?> GetJobByIdAsync(string jobId)
    {
        _logger.LogInformation("🔍 Retrieving job with ID: {JobId}", jobId);
        
        var job = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId);
        
        if (job == null)
        {
            _logger.LogWarning("❌ Job with ID {JobId} not found", jobId);
        }
        else
        {
            _logger.LogInformation("✅ Successfully retrieved job with ID: {JobId}", jobId);
        }
        
        return job;
    }

    public async Task<JobEntity> UpdateJobAsync(JobEntity job)
    {
        _logger.LogInformation("📝 Updating job with ID: {JobId}", job.Id);
        
        job.UpdatedAt = DateTimeOffset.UtcNow;
        _context.Jobs.Update(job);
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("✅ Successfully updated job with ID: {JobId}", job.Id);
        return job;
    }

    public async Task<List<JobEntity>> GetJobsAsync(int page = 1, int pageSize = 20)
    {
        _logger.LogInformation("📋 Retrieving jobs - Page: {Page}, PageSize: {PageSize}", page, pageSize);
        
        var jobs = await _context.Jobs
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        
        _logger.LogInformation("✅ Successfully retrieved {JobCount} jobs", jobs.Count);
        return jobs;
    }

    public async Task<(List<JobEntity> jobs, int totalCount)> GetJobsAsync(
        int page = 1, 
        int pageSize = 20, 
        string? userId = null, 
        JobStatus? status = null, 
        JobType? type = null, 
        string? sort = null, 
        string? order = null)
    {
        _logger.LogInformation("📋 Retrieving jobs with filters - Page: {Page}, PageSize: {PageSize}, Status: {Status}, Type: {Type}, Sort: {Sort}, Order: {Order}", 
            page, pageSize, status, type, sort, order);

        var query = _context.Jobs.AsQueryable();

        // Apply filters
        if (status.HasValue)
        {
            query = query.Where(j => j.Status == status.Value);
            _logger.LogDebug("🔍 Applied status filter: {Status}", status.Value);
        }

        if (type.HasValue)
        {
            query = query.Where(j => j.Type == type.Value);
            _logger.LogDebug("🔍 Applied type filter: {Type}", type.Value);
        }

        // Note: userId filtering is not implemented as JobEntity doesn't have a UserId field
        // This would require extending the model in a future iteration
        if (!string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("⚠️ UserId filtering requested but not supported in current schema: {UserId}", userId);
        }

        // Get total count for pagination
        var totalCount = await query.CountAsync();

        // Apply sorting
        var sortField = sort?.ToLowerInvariant() switch
        {
            "updated_at" => "UpdatedAt",
            "name" => "Name",
            _ => "CreatedAt" // default sort
        };

        var isDescending = order?.ToLowerInvariant() != "asc"; // default to descending

        query = sortField switch
        {
            "UpdatedAt" => isDescending ? query.OrderByDescending(j => j.UpdatedAt) : query.OrderBy(j => j.UpdatedAt),
            "Name" => isDescending ? query.OrderByDescending(j => j.Name) : query.OrderBy(j => j.Name),
            _ => isDescending ? query.OrderByDescending(j => j.CreatedAt) : query.OrderBy(j => j.CreatedAt)
        };

        _logger.LogDebug("📊 Applied sorting: {SortField} {Order}", sortField, isDescending ? "DESC" : "ASC");

        // Apply pagination
        var jobs = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        _logger.LogInformation("✅ Successfully retrieved {JobCount} jobs out of {TotalCount} total", jobs.Count, totalCount);
        return (jobs, totalCount);
    }
}

/// <summary>
/// Entity Framework DbContext for job storage
/// </summary>
public class JobDbContext : DbContext
{
    public DbSet<JobEntity> Jobs { get; set; }

    public JobDbContext(DbContextOptions<JobDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Configure JobEntity
        modelBuilder.Entity<JobEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(50);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.Type).HasConversion<string>();
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.ConfigurationJson).IsRequired();
            entity.Property(e => e.ProgressJson).IsRequired();
            
            // Add indexes for common queries
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}