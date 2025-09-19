using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CopilotEvalApi.Models;
using CopilotEvalApi.Observability;
using System.Diagnostics;

namespace CopilotEvalApi.Services;

public interface ICopilotService
{
    Task<string> CreateConversationAsync(string accessToken);
    Task<CopilotConversation> ChatAsync(string accessToken, string conversationId, CopilotChatRequest request);
    Task<string> GetAuthUrlAsync(string redirectUri, string state);
    Task<TokenResponse> ExchangeCodeForTokenAsync(string code, string redirectUri);
    Task<TeamsAppsResponse> GetInstalledAgentsAsync(string accessToken);
}

public class CopilotService : ICopilotService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CopilotService> _logger;
    private readonly AzureAdOptions _azureAdOptions;
    private readonly MicrosoftGraphOptions _graphOptions;

    public CopilotService(
        HttpClient httpClient, 
        IConfiguration configuration, 
        ILogger<CopilotService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _azureAdOptions = configuration.GetSection("AzureAd").Get<AzureAdOptions>() ?? new AzureAdOptions();
        _graphOptions = configuration.GetSection("MicrosoftGraph").Get<MicrosoftGraphOptions>() ?? new MicrosoftGraphOptions();
    }

    // Helper to resolve tenant id with sensible fallbacks
    private string ResolveTenantId()
    {
        var tenant = _azureAdOptions.TenantId?.Trim() ?? string.Empty;

        // Detect common placeholder values used in the repo
        var isPlaceholder = string.IsNullOrWhiteSpace(tenant)
                            || tenant.IndexOf("YOUR_TENANT", StringComparison.OrdinalIgnoreCase) >= 0
                            || tenant.IndexOf("YOUR_TENANT_ID", StringComparison.OrdinalIgnoreCase) >= 0
                            || tenant.IndexOf("YOUR-" , StringComparison.OrdinalIgnoreCase) >= 0;

        if (!isPlaceholder)
            return tenant;

        // Try environment variable next
        var envTenant = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
        if (!string.IsNullOrWhiteSpace(envTenant))
        {
            _logger.LogWarning("AzureAd:TenantId is not set in configuration or is a placeholder. Using AZURE_TENANT_ID from environment variable.");
            return envTenant.Trim();
        }

        // Last resort: fall back to 'common' to allow multi-tenant sign-in (useful for development), but log guidance
        _logger.LogWarning("AzureAd:TenantId is not set and AZURE_TENANT_ID environment variable is not present. Falling back to 'common'. Set AzureAd:TenantId or AZURE_TENANT_ID to your tenant id to restrict to a single tenant.");
        return "common";
    }

    // Helper to resolve client id with sensible fallbacks
    private string ResolveClientId()
    {
        var clientId = _azureAdOptions.ClientId?.Trim() ?? string.Empty;

        var isPlaceholder = string.IsNullOrWhiteSpace(clientId)
                            || clientId.IndexOf("YOUR_APPLICATION_CLIENT_ID", StringComparison.OrdinalIgnoreCase) >= 0
                            || clientId.IndexOf("YOUR_CLIENT_ID", StringComparison.OrdinalIgnoreCase) >= 0
                            || clientId.IndexOf("YOUR-", StringComparison.OrdinalIgnoreCase) >= 0;

        if (!isPlaceholder)
            return clientId;

        var envClient = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        if (!string.IsNullOrWhiteSpace(envClient))
        {
            _logger.LogWarning("AzureAd:ClientId is not set in configuration or is a placeholder. Using AZURE_CLIENT_ID from environment variable.");
            return envClient.Trim();
        }

        // Emit error-level log since client id is required to build a valid OAuth URL
        _logger.LogError("AzureAd:ClientId is not set and AZURE_CLIENT_ID environment variable is not present. Please set AzureAd:ClientId in configuration or AZURE_CLIENT_ID environment variable.");
        return clientId; // return whatever was present (likely placeholder) to keep current behavior
    }

    public async Task<string> CreateConversationAsync(string accessToken)
    {
        try
        {
            _logger.LogInformation("🎬 Creating new Copilot conversation...");
            
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_graphOptions.BaseUrl}/copilot/conversations");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("Accept", "application/json");
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            _logger.LogInformation("📡 Sending conversation creation request to: {Url}", request.RequestUri);

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("📨 Conversation creation response - Status: {StatusCode}, Content Length: {Length}", 
                response.StatusCode, responseContent.Length);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("❌ Failed to create conversation. Status: {StatusCode}, Content: {Content}", 
                    response.StatusCode, responseContent);
                throw new HttpRequestException($"Failed to create conversation: {response.StatusCode} - {responseContent}");
            }

            var conversationResponse = JsonSerializer.Deserialize<CopilotConversationCreateResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var conversationId = conversationResponse?.Id ?? throw new InvalidOperationException("No conversation ID returned");
            _logger.LogInformation("✅ Successfully created conversation: {ConversationId}", conversationId);

            return conversationId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Error creating Copilot conversation");
            throw;
        }
    }

    public async Task<CopilotConversation> ChatAsync(string accessToken, string conversationId, CopilotChatRequest request)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(Telemetry.Activities.CopilotApiCall);
        activity?.SetTag(Telemetry.Tags.ApiEndpoint, "chat");
        activity?.SetTag("conversation.id", conversationId);
        activity?.SetTag("http.method", "POST");
        activity?.SetTag("http.url", $"{_graphOptions.BaseUrl}/copilot/conversations/{conversationId}/chat");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("💬 Starting chat message to conversation {ConversationId}", conversationId);
            
            // Log the request content (truncated for readability)
            if (request.Message != null)
            {
                var contentPreview = request.Message.Text?.Length > 100 
                    ? request.Message.Text[..100] + "..." 
                    : request.Message.Text;
                _logger.LogInformation("📨 Message: {Content}", contentPreview);
                activity?.SetTag("message.length", request.Message.Text?.Length ?? 0);
            }
            
            if (request.AdditionalContext?.Count > 0)
            {
                _logger.LogInformation("📚 Additional context: {ContextCount} items", request.AdditionalContext.Count);
                activity?.SetTag("context.count", request.AdditionalContext.Count);
                foreach (var (context, index) in request.AdditionalContext.Select((c, i) => (c, i)))
                {
                    var contextPreview = context.Text?.Length > 100 
                        ? context.Text[..100] + "..." 
                        : context.Text;
                    _logger.LogInformation("📝 Context {Index}: {Description} - {Text}", 
                        index + 1, context.Description ?? "No description", contextPreview);
                }
            }

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, 
                $"{_graphOptions.BaseUrl}/copilot/conversations/{conversationId}/chat");
            
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            httpRequest.Headers.Add("Accept", "application/json");

            // Send the chat request directly without the "request" wrapper
            var jsonContent = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            
            activity?.SetTag("http.request.body.size", jsonContent.Length);
            
            _logger.LogInformation("📡 Sending POST request to {Url}", httpRequest.RequestUri);
            _logger.LogInformation("📊 Request payload size: {Size} bytes", jsonContent.Length);
            _logger.LogInformation("📋 Request payload: {Payload}", jsonContent);
            
            // Debug: Let's also check the structure of our objects
            _logger.LogInformation("🔍 Debug - Request.Message.Text: '{Text}'", request.Message?.Text ?? "NULL");

            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            stopwatch.Stop();
            
            activity?.SetTag("http.response.status_code", (int)response.StatusCode);
            activity?.SetTag("http.response.body.size", responseContent.Length);
            
            // Record API duration metrics
            Telemetry.CopilotApiDuration.Record(stopwatch.Elapsed.TotalSeconds, new TagList
            {
                { Telemetry.Tags.ApiEndpoint, "chat" },
                { "http.response.status_code", ((int)response.StatusCode).ToString() },
                { Telemetry.Tags.Status, response.IsSuccessStatusCode ? "success" : "error" }
            });

            _logger.LogInformation("📨 Received response: Status={StatusCode}, Size={Size} bytes", 
                response.StatusCode, responseContent.Length);

            if (!response.IsSuccessStatusCode)
            {
                activity?.SetTag(Telemetry.Tags.Status, "error");
                activity?.SetTag(Telemetry.Tags.ErrorType, "HttpError");
                
                _logger.LogError("💥 Failed to send chat message. Status: {StatusCode}, Content: {Content}", 
                    response.StatusCode, responseContent);
                throw new HttpRequestException($"Failed to send chat: {response.StatusCode} - {responseContent}");
            }

            var chatResponse = JsonSerializer.Deserialize<CopilotConversation>(responseContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (chatResponse != null)
            {
                activity?.SetTag("response.message_count", chatResponse.Messages?.Count ?? 0);
                activity?.SetTag(Telemetry.Tags.Status, "success");
                
                _logger.LogInformation("✅ Chat successful! Response has {MessageCount} messages", 
                    chatResponse.Messages?.Count ?? 0);
                
                // Log response messages (truncated for readability)
                if (chatResponse.Messages != null)
                {
                    foreach (var (message, index) in chatResponse.Messages.Select((m, i) => (m, i)))
                    {
                        var textPreview = message.Text?.Length > 100 
                            ? message.Text[..100] + "..." 
                            : message.Text;
                        _logger.LogInformation("📬 Response {Index}: Id={Id}, Text={Text}", 
                            index + 1, message.Id, textPreview);
                            
                        if (message.Attributions?.Count > 0)
                        {
                            _logger.LogInformation("🔗 Message has {AttributionCount} attributions", 
                                message.Attributions.Count);
                        }
                    }
                }
            }

            return chatResponse ?? throw new InvalidOperationException("No chat response returned");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            // Record API failure metrics
            Telemetry.CopilotApiDuration.Record(stopwatch.Elapsed.TotalSeconds, new TagList
            {
                { Telemetry.Tags.ApiEndpoint, "chat" },
                { Telemetry.Tags.Status, "error" },
                { Telemetry.Tags.ErrorType, ex.GetType().Name }
            });

            activity?.SetTag(Telemetry.Tags.Status, "error");
            activity?.SetTag(Telemetry.Tags.ErrorType, ex.GetType().Name);

            _logger.LogError(ex, "💥 Error sending chat message to conversation {ConversationId}", conversationId);
            throw;
        }
    }

    public async Task<string> GetAuthUrlAsync(string redirectUri, string state)
    {
        _logger.LogInformation("🔐 Generating OAuth authorization URL");
        _logger.LogInformation("📍 Redirect URI: {RedirectUri}", redirectUri);
        _logger.LogInformation("🎲 State: {State}", state);
        
        await Task.CompletedTask; // For async signature

        var scopes = string.Join(" ", _graphOptions.Scopes);
        _logger.LogInformation("🔑 Requested scopes: {Scopes}", scopes);
        
        var tenantId = ResolveTenantId();
        var clientId = ResolveClientId();
        var authUrl = $"{_azureAdOptions.Instance}{tenantId}/oauth2/v2.0/authorize" +
                     $"?client_id={clientId}" +
                      $"&response_type=code" +
                      $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                      $"&response_mode=query" +
                      $"&scope={Uri.EscapeDataString(scopes)}" +
                      $"&state={Uri.EscapeDataString(state)}";

        _logger.LogInformation("✅ OAuth URL generated successfully");
        return authUrl;
    }

    public async Task<TokenResponse> ExchangeCodeForTokenAsync(string code, string redirectUri)
    {
        try
        {
            _logger.LogInformation("🎫 Exchanging authorization code for access token");
            _logger.LogInformation("📍 Redirect URI: {RedirectUri}", redirectUri);
            _logger.LogInformation("🔑 Authorization code length: {CodeLength} characters", code?.Length ?? 0);
            
            var tenantIdForToken = ResolveTenantId();
            var clientIdForToken = ResolveClientId();
            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, 
                $"{_azureAdOptions.Instance}{tenantIdForToken}/oauth2/v2.0/token");

            var formData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", clientIdForToken),
                new KeyValuePair<string, string>("scope", string.Join(" ", _graphOptions.Scopes)),
                new KeyValuePair<string, string>("code", code ?? string.Empty),
                new KeyValuePair<string, string>("redirect_uri", redirectUri),
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("client_secret", _azureAdOptions.ClientSecret)
            });

            tokenRequest.Content = formData;

            _logger.LogInformation("📡 Sending token exchange request to Azure AD");

            var response = await _httpClient.SendAsync(tokenRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("📨 Token exchange response: Status={StatusCode}, Size={Size} bytes", 
                response.StatusCode, responseContent.Length);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("💥 Failed to exchange code for token. Status: {StatusCode}, Content: {Content}", 
                    response.StatusCode, responseContent);
                throw new HttpRequestException($"Failed to exchange code for token: {response.StatusCode} - {responseContent}");
            }

            var tokenData = JsonSerializer.Deserialize<JsonElement>(responseContent);
            
            var tokenResponse = new TokenResponse(
                AccessToken: tokenData.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("No access token"),
                TokenType: tokenData.GetProperty("token_type").GetString() ?? "Bearer",
                ExpiresIn: tokenData.GetProperty("expires_in").GetInt32(),
                RefreshToken: tokenData.TryGetProperty("refresh_token", out var refreshToken) ? refreshToken.GetString() : null
            );
            
            _logger.LogInformation("✅ Successfully exchanged code for token");
            _logger.LogInformation("⏰ Token expires in: {ExpiresIn} seconds", tokenResponse.ExpiresIn);
            _logger.LogInformation("🔄 Refresh token available: {HasRefreshToken}", !string.IsNullOrEmpty(tokenResponse.RefreshToken));
            
            return tokenResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Error exchanging authorization code for token");
            throw;
        }
    }

    public async Task<TeamsAppsResponse> GetInstalledAgentsAsync(string accessToken)
    {
        try
        {
            _logger.LogInformation("🤖 Retrieving installed Copilot agents from tenant...");
            
            // Query for Teams apps that are installed in the tenant
            // This includes Copilot agents/plugins
            var request = new HttpRequestMessage(HttpMethod.Get, 
                $"{_graphOptions.BaseUrl}/appCatalogs/teamsApps?$expand=appDefinitions&$filter=distributionMethod eq 'organization' or distributionMethod eq 'sideloaded' or distributionMethod eq 'store'");
            
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("Accept", "application/json");
            
            _logger.LogInformation("📡 Sending request to Microsoft Graph: {Url}", request.RequestUri);
            
            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            _logger.LogInformation("📨 Teams Apps response - Status: {StatusCode}, Content Length: {Length}",
                response.StatusCode, responseContent.Length);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("❌ Failed to retrieve Teams apps. Status: {StatusCode}, Content: {Content}",
                    response.StatusCode, responseContent);
                throw new HttpRequestException($"Failed to retrieve Teams apps: {response.StatusCode} - {responseContent}");
            }
            
            var teamsAppsResponse = JsonSerializer.Deserialize<TeamsAppsResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            if (teamsAppsResponse != null)
            {
                _logger.LogInformation("✅ Retrieved {AppCount} Teams apps/agents", teamsAppsResponse.Value?.Count ?? 0);
                
                // Log some details about found apps
                if (teamsAppsResponse.Value?.Count > 0)
                {
                    foreach (var app in teamsAppsResponse.Value.Take(5)) // Log first 5 for brevity
                    {
                        _logger.LogInformation("📱 App: {DisplayName} (ID: {Id}, Distribution: {Distribution})",
                            app.DisplayName, app.Id, app.DistributionMethod);
                    }
                    
                    if (teamsAppsResponse.Value.Count > 5)
                    {
                        _logger.LogInformation("... and {MoreCount} more apps", teamsAppsResponse.Value.Count - 5);
                    }
                }
                
                return teamsAppsResponse;
            }
            
            _logger.LogWarning("⚠️ Received null response when deserializing Teams apps");
            return new TeamsAppsResponse(new List<TeamsApp>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Error retrieving installed agents");
            throw;
        }
    }
}
