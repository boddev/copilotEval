using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using CopilotEvalApi.Controllers;
using CopilotEvalApi.Models;
using CopilotEvalApi.Services;
using CopilotEvalApi.Repositories;

namespace CopilotEvalApi.Tests.Controllers;

public class JobsControllerTests
{
    private readonly Mock<IJobRepository> _mockJobRepository;
    private readonly Mock<IJobQueueService> _mockJobQueueService;
    private readonly Mock<ILogger<JobsController>> _mockLogger;
    private readonly JobsController _controller;

    public JobsControllerTests()
    {
        _mockJobRepository = new Mock<IJobRepository>();
        _mockJobQueueService = new Mock<IJobQueueService>();
        _mockLogger = new Mock<ILogger<JobsController>>();
        _controller = new JobsController(_mockJobRepository.Object, _mockJobQueueService.Object, _mockLogger.Object);
        
        // Setup HttpContext for the controller
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Fact]
    public async Task GetJobById_ExistingJob_ReturnsOkResult()
    {
        // Arrange
        var jobId = "job_test123";
        var job = new JobEntity
        {
            Id = jobId,
            Name = "Test Job",
            Status = JobStatus.Pending,
            Type = JobType.BulkEvaluation,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ConfigurationJson = "{}",
            ProgressJson = "{\"total_items\":0,\"completed_items\":0,\"percentage\":0}"
        };

        _mockJobRepository.Setup(x => x.GetJobByIdAsync(jobId))
            .ReturnsAsync(job);

        // Act
        var result = await _controller.GetJob(jobId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        okResult!.Value.Should().BeOfType<Job>();
        
        var returnedJob = okResult.Value as Job;
        returnedJob!.Id.Should().Be(jobId);
        returnedJob.Name.Should().Be("Test Job");
    }

    [Fact]
    public async Task GetJobById_NonExistentJob_ReturnsNotFound()
    {
        // Arrange
        var jobId = "job_nonexistent";
        
        _mockJobRepository.Setup(x => x.GetJobByIdAsync(jobId))
            .ReturnsAsync((JobEntity?)null);

        // Act
        var result = await _controller.GetJob(jobId);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetJobs_ValidCall_ReturnsOkResult()
    {
        // Arrange
        var jobs = new List<JobEntity>
        {
            new() { Id = "job1", Name = "Job 1", Status = JobStatus.Pending, Type = JobType.BulkEvaluation, 
                    CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, ConfigurationJson = "{}", ProgressJson = "{}" },
            new() { Id = "job2", Name = "Job 2", Status = JobStatus.Running, Type = JobType.BulkEvaluation, 
                    CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, ConfigurationJson = "{}", ProgressJson = "{}" }
        };

        var jobsResult = (jobs, totalCount: 2);

        _mockJobRepository.Setup(x => x.GetJobsAsync(
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<string?>(),
            It.IsAny<JobStatus?>(),
            It.IsAny<JobType?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>()))
            .ReturnsAsync(jobsResult);

        // Act
        var result = await _controller.GetJobs();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void Controller_Construction_ShouldSucceed()
    {
        // Act & Assert
        _controller.Should().NotBeNull();
    }
}