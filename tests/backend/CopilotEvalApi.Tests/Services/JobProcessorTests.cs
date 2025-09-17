using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;
using FluentAssertions;
using CopilotEvalApi.Services;

namespace CopilotEvalApi.Tests.Services;

public class JobProcessorTests
{
    private readonly Mock<ILogger<JobProcessor>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IConfigurationSection> _mockConnectionStringSection;

    public JobProcessorTests()
    {
        _mockLogger = new Mock<ILogger<JobProcessor>>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockConnectionStringSection = new Mock<IConfigurationSection>();
        
        // Setup configuration to return "InMemory" for Service Bus connection string
        _mockConnectionStringSection.Setup(c => c.Value).Returns("InMemory");
        _mockConfiguration.Setup(c => c.GetSection("ConnectionStrings:ServiceBus"))
            .Returns(_mockConnectionStringSection.Object);
    }

    [Fact]
    public void JobProcessor_Construction_ShouldSucceed()
    {
        // Act
        var jobProcessor = new JobProcessor(_mockLogger.Object, _mockConfiguration.Object, _mockServiceProvider.Object);

        // Assert
        jobProcessor.Should().NotBeNull();
    }

    [Fact]
    public void JobProcessor_ConfigurationSection_ShouldBeAccessible()
    {
        // Arrange & Act
        var section = _mockConfiguration.Object.GetSection("ConnectionStrings:ServiceBus");

        // Assert
        section.Should().NotBeNull();
    }
}