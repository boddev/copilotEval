using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using System.Net.Http;

namespace CopilotEvalApi.Tests;

public class ApiIntegrationTest : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiIntegrationTest(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Get_Jobs_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/jobs");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_NonExistentJob_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/jobs/nonexistent");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}