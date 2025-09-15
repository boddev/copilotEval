using System.Text;
using System.Text.Json;
using CopilotEvalApi.Models;

namespace CopilotEvalApi.Services;

public class GraphSearchService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GraphSearchService> _logger;

    public GraphSearchService(HttpClient httpClient, ILogger<GraphSearchService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<ExternalConnection>> GetExternalConnectionsAsync(string accessToken)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.GetAsync("https://graph.microsoft.com/v1.0/external/connections");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to retrieve external connections. Status: {StatusCode}, Content: {Content}", 
                    response.StatusCode, errorContent);
                
                // Provide specific guidance for permission errors
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    throw new UnauthorizedAccessException(
                        "Access denied to Microsoft Graph external connections. " +
                        "Required permissions: ExternalConnection.Read.All or ExternalConnection.ReadWrite.All. " +
                        "Please ensure your application registration has these permissions and admin consent has been granted.");
                }
                
                throw new HttpRequestException($"Failed to retrieve external connections. Status: {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var connectionsResponse = JsonSerializer.Deserialize<ExternalConnectionsResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return connectionsResponse?.Value ?? new List<ExternalConnection>();
        }
        catch (UnauthorizedAccessException)
        {
            throw; // Re-throw permission errors as-is
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving external connections");
            throw new InvalidOperationException($"Error retrieving external connections: {ex.Message}", ex);
        }
    }

    public async Task<List<SearchHit>> SearchKnowledgeSourceAsync(string accessToken, string connectionId, string query, int maxResults = 5)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

            var searchRequest = new GraphSearchRequest(
                Requests: new List<SearchRequestItem>
                {
                    new SearchRequestItem(
                        EntityTypes: new List<string> { "externalItem" },
                        ContentSources: new List<string> { $"/external/connections/{connectionId}" },
                        Query: new SearchQuery(QueryString: query),
                        From: 0,
                        Size: maxResults,
                        Fields: new List<string> { "title", "content", "url" }
                    )
                }
            );

            var json = JsonSerializer.Serialize(searchRequest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://graph.microsoft.com/v1.0/search/query", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to search knowledge source. Status: {StatusCode}, Content: {Content}", 
                    response.StatusCode, await response.Content.ReadAsStringAsync());
                return new List<SearchHit>();
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var searchResponse = JsonSerializer.Deserialize<GraphSearchResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var hits = new List<SearchHit>();
            if (searchResponse?.Value != null)
            {
                foreach (var container in searchResponse.Value)
                {
                    foreach (var hitsContainer in container.HitsContainers)
                    {
                        hits.AddRange(hitsContainer.Hits);
                    }
                }
            }

            return hits;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching knowledge source: {ConnectionId}", connectionId);
            return new List<SearchHit>();
        }
    }

    public string FormatSearchResultsAsContext(List<SearchHit> searchResults, string connectionName)
    {
        if (!searchResults.Any())
        {
            return $"No relevant information found in {connectionName} knowledge source.";
        }

        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine($"Relevant information from {connectionName} knowledge source:");
        contextBuilder.AppendLine();

        for (int i = 0; i < searchResults.Count; i++)
        {
            var hit = searchResults[i];
            contextBuilder.AppendLine($"[Result {i + 1}]");
            
            if (!string.IsNullOrEmpty(hit.Summary))
            {
                contextBuilder.AppendLine(hit.Summary);
            }
            else if (hit.Resource != null)
            {
                // Try to extract useful information from the resource object
                var resourceJson = JsonSerializer.Serialize(hit.Resource);
                contextBuilder.AppendLine($"Resource: {resourceJson}");
            }
            
            contextBuilder.AppendLine();
        }

        contextBuilder.AppendLine("Please base your response primarily on the information provided above from the selected knowledge source.");
        
        return contextBuilder.ToString();
    }
}
