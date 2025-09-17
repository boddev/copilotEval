using Xunit;
using FluentAssertions;
using CopilotEvalApi.Models;

namespace CopilotEvalApi.Tests.Models;

public class JobModelsTests
{
    [Fact]
    public void JobEntity_DefaultValues_ShouldBeSet()
    {
        // Arrange & Act
        var job = new JobEntity
        {
            Id = "job_test123",
            Name = "Test Job",
            Type = JobType.BulkEvaluation,
            Status = JobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ConfigurationJson = "{}",
            ProgressJson = "{}"
        };

        // Assert
        job.Id.Should().Be("job_test123");
        job.Name.Should().Be("Test Job");
        job.Type.Should().Be(JobType.BulkEvaluation);
        job.Status.Should().Be(JobStatus.Pending);
    }

    [Fact]
    public void JobMessage_Creation_ShouldHaveCorrectProperties()
    {
        // Arrange
        var jobId = "job_test123";
        var messageType = JobMessageType.JobCreated;
        var createdAt = DateTimeOffset.UtcNow;
        var payload = new { test = "data" };

        // Act
        var message = new JobMessage(jobId, messageType, createdAt, payload)
        {
            CorrelationId = Guid.NewGuid()
        };

        // Assert
        message.JobId.Should().Be(jobId);
        message.MessageType.Should().Be(messageType);
        message.CreatedAt.Should().Be(createdAt);
        message.Payload.Should().Be(payload);
        message.CorrelationId.Should().NotBeNull();
    }

    [Theory]
    [InlineData(JobStatus.Pending)]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    public void JobStatus_AllValues_ShouldBeValid(JobStatus status)
    {
        // Arrange
        var job = new JobEntity
        {
            Id = "job_test",
            Name = "Test Job",
            Type = JobType.BulkEvaluation,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ConfigurationJson = "{}",
            ProgressJson = "{}"
        };

        // Act & Assert
        job.Status.Should().Be(status);
    }

    [Theory]
    [InlineData(JobType.BulkEvaluation)]
    [InlineData(JobType.SingleEvaluation)]
    [InlineData(JobType.BatchProcessing)]
    public void JobType_AllValues_ShouldBeValid(JobType type)
    {
        // Arrange
        var job = new JobEntity
        {
            Id = "job_test",
            Name = "Test Job",
            Type = type,
            Status = JobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ConfigurationJson = "{}",
            ProgressJson = "{}"
        };

        // Act & Assert
        job.Type.Should().Be(type);
    }
}