using Microsoft.AspNetCore.Cors;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using CopilotEvalApi.Services;
using CopilotEvalApi.Models;
using CopilotEvalApi.Repositories;
using CopilotEvalApi.Observability;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;

var builder = WebApplication.CreateBuilder(args);

// Configure Azure Key Vault for production
if (!builder.Environment.IsDevelopment())
{
    var keyVaultUrl = builder.Configuration["KeyVault:VaultUrl"];
    if (!string.IsNullOrEmpty(keyVaultUrl))
    {
        // Use Managed Identity in production
        var credential = new DefaultAzureCredential();
        builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUrl), credential);
    }
}

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    });

// Add Entity Framework with SQL Server database
builder.Services.AddDbContext<JobDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrEmpty(connectionString))
    {
        // Fallback to In-Memory database if no connection string is configured
        options.UseInMemoryDatabase("CopilotEvalDb");
    }
    else
    {
        options.UseSqlServer(connectionString);
    }
});

// Configure Azure AD Authentication
// Capture tenant and client fallback details here so we can log them after building the app using the real ILogger
string? tenantFallbackMessage = null;
string? clientFallbackMessage = null;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var azureAdConfig = builder.Configuration.GetSection("AzureAd");
        var tenantId = azureAdConfig["TenantId"];
        var clientId = azureAdConfig["ClientId"];
        var audience = azureAdConfig["Audience"] ?? clientId;

        // Capture any tenant fallback details so we can log them via the application's ILogger after the app is built.
        // Avoid building a temporary service provider during configuration to prevent duplicating singleton services.
        // tenantFallbackMessage is declared in the outer scope and will be assigned here if needed
        if (string.IsNullOrWhiteSpace(tenantId)
            || tenantId.IndexOf("YOUR_TENANT", StringComparison.OrdinalIgnoreCase) >= 0
            || tenantId.IndexOf("YOUR_TENANT_ID", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var envTenant = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
            if (!string.IsNullOrWhiteSpace(envTenant))
            {
                tenantId = envTenant.Trim();
                tenantFallbackMessage = "AzureAd:TenantId was missing or a placeholder. Using AZURE_TENANT_ID environment variable.";
            }
            else
            {
                // Fall back to 'common' for development/multi-tenant scenarios but capture a message to log later
                tenantId = "common";
                tenantFallbackMessage = "AzureAd:TenantId was missing or a placeholder. Falling back to 'common'. Set AzureAd:TenantId or AZURE_TENANT_ID to restrict to a tenant.";
            }
        }

        // Resolve ClientId similarly so audience/token validation and auth URL generation pick up env var when needed
        if (string.IsNullOrWhiteSpace(clientId)
            || clientId.IndexOf("YOUR_APPLICATION_CLIENT_ID", StringComparison.OrdinalIgnoreCase) >= 0
            || clientId.IndexOf("YOUR_CLIENT_ID", StringComparison.OrdinalIgnoreCase) >= 0
            || clientId.IndexOf("YOUR-", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var envClient = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
            if (!string.IsNullOrWhiteSpace(envClient))
            {
                clientId = envClient.Trim();
                clientFallbackMessage = "AzureAd:ClientId was missing or a placeholder. Using AZURE_CLIENT_ID environment variable.";
            }
            else
            {
                clientFallbackMessage = "AzureAd:ClientId was missing or a placeholder. Set AzureAd:ClientId or AZURE_CLIENT_ID environment variable to a valid client id.";
            }
        }

        options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"https://login.microsoftonline.com/{tenantId}/v2.0",
            ValidateAudience = true,
            ValidAudiences = new[] { audience, $"api://{audience}" },
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError("Authentication failed: {Error}", context.Exception.Message);
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Token validated for user: {User}", context.Principal?.Identity?.Name ?? "Unknown");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("copiloteval-backend", 
                   serviceVersion: "1.0.0",
                   serviceInstanceId: Environment.MachineName))
    .WithTracing(tracing => tracing
        .AddSource(Telemetry.ActivitySource.Name)
        .AddSource(WorkerTelemetry.ActivitySource.Name)
        .AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;
            options.EnrichWithHttpRequest = (activity, request) =>
            {
                activity.SetTag("http.request.body.size", request.ContentLength);
                activity.SetTag("user_agent", request.Headers.UserAgent.ToString());
            };
            options.EnrichWithHttpResponse = (activity, response) =>
            {
                activity.SetTag("http.response.body.size", response.ContentLength);
            };
        })
        .AddHttpClientInstrumentation(options =>
        {
            options.RecordException = true;
            options.EnrichWithHttpRequestMessage = (activity, request) =>
            {
                activity.SetTag("http.request.body.size", request.Content?.Headers?.ContentLength);
            };
            options.EnrichWithHttpResponseMessage = (activity, response) =>
            {
                activity.SetTag("http.response.body.size", response.Content?.Headers?.ContentLength);
            };
        })
        .AddEntityFrameworkCoreInstrumentation(options =>
        {
            options.SetDbStatementForText = true;
            options.EnrichWithIDbCommand = (activity, command) =>
            {
                activity.SetTag("db.command.timeout", command.CommandTimeout);
            };
        }))
    .WithMetrics(metrics => metrics
        .AddMeter(Telemetry.Meter.Name)
        .AddMeter(WorkerTelemetry.Meter.Name)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());

// Configure Application Insights if connection string is available
var appInsightsConnectionString = builder.Configuration.GetConnectionString("ApplicationInsights");
if (!string.IsNullOrEmpty(appInsightsConnectionString))
{
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri("https://dc.applicationinsights.azure.com/v2/track");
            }))
        .WithMetrics(metrics => metrics
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri("https://dc.applicationinsights.azure.com/v2/track");
            }));
}

// Add CORS
builder.Services.AddCors(options =>
{
    var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
        ?? new[] { "http://localhost:3000", "http://localhost:3001", "http://localhost:5173", "https://copilotevalst9571.z14.web.core.windows.net", "https://copilotevalweb5229.azurewebsites.net" };
    
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add HTTP client for external API calls
builder.Services.AddHttpClient<ICopilotService, CopilotService>();
builder.Services.AddHttpClient<GraphSearchService>();

// Register services
builder.Services.AddScoped<ICopilotService, CopilotService>();
builder.Services.AddScoped<GraphSearchService>();
builder.Services.AddScoped<IJobRepository, JobRepository>();
builder.Services.AddScoped<IJobQueueService, JobQueueService>();
builder.Services.AddScoped<IExecutionService, ExecutionService>();

// Register background services
builder.Services.AddHostedService<JobProcessor>();

var app = builder.Build();

// Ensure database is created for development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<JobDbContext>();
    context.Database.EnsureCreated();
}

// Trigger rebuild to see latest logs
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("🚀 Copilot Evaluation API starting up...");
// Emit any tenant fallback messages using the real ILogger (ensures messages go through Application Insights if configured)
if (!string.IsNullOrWhiteSpace(tenantFallbackMessage))
{
    logger.LogWarning(tenantFallbackMessage);
}
if (!string.IsNullOrWhiteSpace(clientFallbackMessage))
{
    logger.LogWarning(clientFallbackMessage);
}

// Log the CORS origins the app actually loaded at runtime
var corsOriginsLoaded = app.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
logger.LogInformation("CORS origins loaded at startup: {Origins}", string.Join(", ", corsOriginsLoaded));

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Only use HTTPS redirection in production
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowReactApp");

// Add authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// Map controllers
app.MapControllers();

// API Endpoints
app.MapGet("/api/health", () => new { Status = "Healthy", Timestamp = DateTime.UtcNow });

// Debug endpoint to test JSON serialization
app.MapPost("/api/debug/json", (ChatRequest request, ILogger<Program> logger) =>
{
    logger.LogInformation("🔍 Debug JSON endpoint called");
    
    // Prepare additional context if instructions are provided
    List<CopilotContextMessage>? additionalContext = null;
    if (!string.IsNullOrWhiteSpace(request.AdditionalInstructions))
    {
        additionalContext = new List<CopilotContextMessage>
        {
            new CopilotContextMessage(
                Text: request.AdditionalInstructions,
                Description: "Additional agent instructions or context"
            )
        };
    }
    
    var chatRequest = new CopilotChatRequest(
        Message: new CopilotConversationRequestMessage(request.Prompt ?? ""),
        AdditionalContext: additionalContext,
        LocationHint: new CopilotConversationLocation(
            Latitude: null,
            Longitude: null,
            TimeZone: request.TimeZone ?? "UTC",
            CountryOrRegion: null,
            CountryOrRegionConfidence: null
        )
    );
    
    var apiRequest = new CopilotChatApiRequest(chatRequest);
    var jsonContent = JsonSerializer.Serialize(apiRequest, new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });
    
    logger.LogInformation("📋 Generated JSON: {Json}", jsonContent);
    
    return Results.Ok(new { GeneratedJson = jsonContent });
});

// OAuth Authentication Endpoints
app.MapGet("/api/auth/url", async (ICopilotService copilotService, string? redirectUri, ILogger<Program> logger) =>
{
    var requestId = Guid.NewGuid().ToString("N")[..8];
    logger.LogInformation("🔐 [Auth {RequestId}] Generating OAuth URL", requestId);
    logger.LogInformation("🔗 [Auth {RequestId}] Redirect URI: {RedirectUri}", requestId, redirectUri ?? "DEFAULT");
    
    try
    {
        var baseUrl = redirectUri ?? "http://localhost:5173";
        var state = Guid.NewGuid().ToString();
        
        logger.LogInformation("🎲 [Auth {RequestId}] Generated state: {State}", requestId, state);
        
        var authUrl = await copilotService.GetAuthUrlAsync(baseUrl, state);
        
        logger.LogInformation("✅ [Auth {RequestId}] OAuth URL generated successfully", requestId);
        logger.LogInformation("🌐 [Auth {RequestId}] Auth URL: {AuthUrl}", requestId, authUrl.Substring(0, Math.Min(authUrl.Length, 100)) + "...");
        
        return Results.Ok(new AuthUrlResponse(authUrl, state));
    }
    catch (Exception ex)
    {
        logger.LogError("💥 [Auth {RequestId}] Error generating OAuth URL: {Error}", requestId, ex.Message);
        return Results.BadRequest(new { Error = ex.Message });
    }
});

app.MapPost("/api/auth/token", async (ICopilotService copilotService, TokenRequest request, ILogger<Program> logger) =>
{
    var requestId = Guid.NewGuid().ToString("N")[..8];
    logger.LogInformation("🎫 [Token {RequestId}] Exchanging code for token", requestId);
    logger.LogInformation("🔑 [Token {RequestId}] Code: {Code}", requestId, request.Code?.Substring(0, Math.Min(request.Code?.Length ?? 0, 20)) + "...");
    logger.LogInformation("🎲 [Token {RequestId}] State: {State}", requestId, request.State);
    logger.LogInformation("🔗 [Token {RequestId}] Redirect URI: {RedirectUri}", requestId, request.RedirectUri ?? "DEFAULT");
    
    try
    {
        var redirectUri = request.RedirectUri ?? "http://localhost:5173";
        
        logger.LogInformation("🌐 [Token {RequestId}] Calling M365 token endpoint...", requestId);
        var startTime = DateTime.UtcNow;
        
        var tokenResponse = await copilotService.ExchangeCodeForTokenAsync(request.Code ?? "", redirectUri);
        
        var duration = DateTime.UtcNow - startTime;
        logger.LogInformation("⚡ [Token {RequestId}] Token exchange completed in {Duration}ms", requestId, duration.TotalMilliseconds);
        logger.LogInformation("✅ [Token {RequestId}] Access token obtained - Type: {TokenType}, Expires in: {ExpiresIn}s", 
            requestId, tokenResponse.TokenType, tokenResponse.ExpiresIn);
        
        return Results.Ok(tokenResponse);
    }
    catch (Exception ex)
    {
        logger.LogError("💥 [Token {RequestId}] Error exchanging token: {Error}", requestId, ex.Message);
        logger.LogError("🔍 [Token {RequestId}] Exception Details: {Details}", requestId, ex.ToString());
        return Results.BadRequest(new { Error = ex.Message });
    }
});

// Get Installed Copilot Agents/Plugins Endpoint
app.MapGet("/api/copilot/agents", async (ICopilotService copilotService, string? accessToken, ILogger<Program> logger) =>
{
    var requestId = Guid.NewGuid().ToString("N")[..8];
    logger.LogInformation("🤖 [Agents {RequestId}] Starting request to get installed agents", requestId);
    logger.LogInformation("🔑 [Agents {RequestId}] Has access token: {HasToken}", requestId, !string.IsNullOrEmpty(accessToken));
    
    try
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            logger.LogWarning("❌ [Agents {RequestId}] Access token is missing", requestId);
            return Results.BadRequest(new { Error = "Access token is required to retrieve installed agents" });
        }

        logger.LogInformation("🔍 [Agents {RequestId}] Querying Microsoft Graph for Teams apps...", requestId);
        var startTime = DateTime.UtcNow;
        
        var agentsResponse = await copilotService.GetInstalledAgentsAsync(accessToken);
        
        var duration = DateTime.UtcNow - startTime;
        logger.LogInformation("⚡ [Agents {RequestId}] Agents query completed in {Duration}ms", requestId, duration.TotalMilliseconds);
        logger.LogInformation("📊 [Agents {RequestId}] Found {AgentCount} agents/apps", requestId, agentsResponse.Value?.Count ?? 0);
        
        return Results.Ok(agentsResponse);
    }
    catch (Exception ex)
    {
        logger.LogError("💥 [Agents {RequestId}] Error retrieving agents: {Error}", requestId, ex.Message);
        logger.LogError("🔍 [Agents {RequestId}] Exception Details: {Details}", requestId, ex.ToString());
        return Results.BadRequest(new { Error = ex.Message });
    }
}).RequireAuthorization();

// Get External Knowledge Sources Endpoint
app.MapGet("/api/copilot/knowledge-sources", async (GraphSearchService graphSearchService, string? accessToken, ILogger<Program> logger) =>
{
    var requestId = Guid.NewGuid().ToString("N")[..8];
    logger.LogInformation("🔍 [KnowledgeSources {RequestId}] Starting request to get external connections", requestId);
    logger.LogInformation("🔑 [KnowledgeSources {RequestId}] Has access token: {HasToken}", requestId, !string.IsNullOrEmpty(accessToken));
    
    try
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            logger.LogWarning("❌ [KnowledgeSources {RequestId}] Access token is missing", requestId);
            return Results.BadRequest(new { Error = "Access token is required to retrieve knowledge sources" });
        }

        logger.LogInformation("🔍 [KnowledgeSources {RequestId}] Querying Microsoft Graph for external connections...", requestId);
        var startTime = DateTime.UtcNow;
        
        var connections = await graphSearchService.GetExternalConnectionsAsync(accessToken);
        
        var duration = DateTime.UtcNow - startTime;
        logger.LogInformation("⚡ [KnowledgeSources {RequestId}] Knowledge sources query completed in {Duration}ms", requestId, duration.TotalMilliseconds);
        logger.LogInformation("📊 [KnowledgeSources {RequestId}] Found {ConnectionCount} external connections", requestId, connections.Count);
        
        return Results.Ok(new { value = connections });
    }
    catch (UnauthorizedAccessException ex)
    {
        logger.LogWarning("🔐 [KnowledgeSources {RequestId}] Permission denied: {Error}", requestId, ex.Message);
        return Results.Problem(
            detail: ex.Message,
            statusCode: 403,
            title: "Permission Required"
        );
    }
    catch (Exception ex)
    {
        logger.LogError("💥 [KnowledgeSources {RequestId}] Error retrieving knowledge sources: {Error}", requestId, ex.Message);
        logger.LogError("🔍 [KnowledgeSources {RequestId}] Exception Details: {Details}", requestId, ex.ToString());
        return Results.BadRequest(new { Error = ex.Message });
    }
});

// Enhanced Copilot Chat Endpoint
app.MapPost("/api/copilot/chat", async (ICopilotService copilotService, GraphSearchService graphSearchService, ChatRequest request, ILogger<Program> logger) =>
{
    var requestId = Guid.NewGuid().ToString("N")[..8];
    logger.LogInformation("🔥 [Request {RequestId}] Starting Copilot chat request", requestId);
    logger.LogInformation("📝 [Request {RequestId}] Prompt: '{Prompt}'", requestId, request.Prompt?.Substring(0, Math.Min(request.Prompt.Length, 100)) + (request.Prompt?.Length > 100 ? "..." : ""));
    logger.LogInformation("🔑 [Request {RequestId}] Has access token: {HasToken}", requestId, !string.IsNullOrEmpty(request.AccessToken));
    logger.LogInformation("💬 [Request {RequestId}] Conversation ID: {ConversationId}", requestId, request.ConversationId ?? "NEW");
    logger.LogInformation("🗃️ [Request {RequestId}] Selected knowledge source: {KnowledgeSource}", requestId, request.SelectedKnowledgeSource ?? "None");
    
    try
    {
        if (string.IsNullOrEmpty(request.AccessToken))
        {
            logger.LogWarning("❌ [Request {RequestId}] Access token is missing", requestId);
            return Results.BadRequest(new ChatResponse(
                Response: "",
                Success: false,
                Error: "Access token is required for M365 Copilot API calls",
                ConversationId: null,
                Attributions: null
            ));
        }

        logger.LogInformation("🔄 [Request {RequestId}] Processing conversation setup...", requestId);
        
        // Create conversation if not provided
        string conversationId;
        if (string.IsNullOrEmpty(request.ConversationId))
        {
            logger.LogInformation("➕ [Request {RequestId}] Creating new conversation...", requestId);
            conversationId = await copilotService.CreateConversationAsync(request.AccessToken);
            logger.LogInformation("✅ [Request {RequestId}] Created conversation: {ConversationId}", requestId, conversationId);
        }
        else
        {
            conversationId = request.ConversationId;
            logger.LogInformation("🔄 [Request {RequestId}] Using existing conversation: {ConversationId}", requestId, conversationId);
        }

        // Prepare the chat request
        logger.LogInformation("📤 [Request {RequestId}] Preparing chat request for M365 Copilot...", requestId);
        
        // Note: SelectedAgentId is not supported by the M365 Copilot Chat API
        // All requests go to the default M365 Copilot experience
        if (!string.IsNullOrEmpty(request.SelectedAgentId))
        {
            logger.LogWarning("⚠️ [Request {RequestId}] SelectedAgentId '{AgentId}' specified but not supported by M365 Copilot Chat API. Using default Copilot.", 
                requestId, request.SelectedAgentId);
        }
        
        // Prepare the message text, incorporating instructions directly into the prompt
        var messageText = request.Prompt ?? "";
        
        // If additional instructions are provided, prepend them to the main prompt
        if (!string.IsNullOrWhiteSpace(request.AdditionalInstructions))
        {
            messageText = $"{request.AdditionalInstructions}\n\n{messageText}";
            logger.LogInformation("📋 [Request {RequestId}] Embedded instructions in prompt. Total length: {Length} chars", 
                requestId, messageText.Length);
        }
        // Search knowledge source if specified and embed results in prompt
        if (!string.IsNullOrWhiteSpace(request.SelectedKnowledgeSource))
        {
            logger.LogInformation("🔍 [Request {RequestId}] Searching knowledge source: {ConnectionId}", requestId, request.SelectedKnowledgeSource);
            
            var searchResults = await graphSearchService.SearchKnowledgeSourceAsync(
                request.AccessToken, 
                request.SelectedKnowledgeSource, 
                request.Prompt ?? "", // Use original prompt for search, not the modified one
                5 // Max results
            );
            
            if (searchResults.Any())
            {
                logger.LogInformation("📊 [Request {RequestId}] Found {ResultCount} results from knowledge source", requestId, searchResults.Count);
                
                var knowledgeContext = graphSearchService.FormatSearchResultsAsContext(searchResults, request.SelectedKnowledgeSource);
                
                // Embed knowledge source results directly in the prompt
                messageText = $"Based on the following information from {request.SelectedKnowledgeSource}:\n\n{knowledgeContext}\n\n{messageText}";
                
                logger.LogInformation("📚 [Request {RequestId}] Embedded knowledge source results in prompt. Total length: {Length} chars", 
                    requestId, messageText.Length);
            }
            else
            {
                logger.LogWarning("⚠️ [Request {RequestId}] No results found in knowledge source: {ConnectionId}", requestId, request.SelectedKnowledgeSource);
                
                // Inform the user that no knowledge was found
                messageText = $"Note: No information was found in {request.SelectedKnowledgeSource} knowledge source.\n\n{messageText}";
            }
        }
        
        var chatRequest = new CopilotChatRequest(
            Message: new CopilotConversationRequestMessage(messageText),
            AdditionalContext: null, // All context now embedded in main message
            LocationHint: new CopilotConversationLocation(
                Latitude: null,
                Longitude: null,
                TimeZone: request.TimeZone ?? "UTC",
                CountryOrRegion: null,
                CountryOrRegionConfidence: null
            )
        );

        logger.LogInformation("📤 [Request {RequestId}] Final prompt prepared with length: {Length} chars", requestId, messageText.Length);

        logger.LogInformation("🌐 [Request {RequestId}] Sending request to M365 Copilot API...", requestId);
        var startTime = DateTime.UtcNow;
        
        // Send chat to M365 Copilot
        var response = await copilotService.ChatAsync(request.AccessToken, conversationId, chatRequest);
        
        var duration = DateTime.UtcNow - startTime;
        logger.LogInformation("⚡ [Request {RequestId}] M365 Copilot API responded in {Duration}ms", requestId, duration.TotalMilliseconds);

        // Get the latest response message
        var latestMessage = response.Messages.LastOrDefault();
        var responseText = latestMessage?.Text ?? "No response received";
        
        logger.LogInformation("📨 [Request {RequestId}] Response received - Length: {Length} chars", requestId, responseText.Length);
        logger.LogInformation("📄 [Request {RequestId}] Response preview: '{ResponsePreview}'", requestId, responseText.Substring(0, Math.Min(responseText.Length, 200)) + (responseText.Length > 200 ? "..." : ""));
        
        if (latestMessage?.Attributions?.Any() == true)
        {
            logger.LogInformation("🔗 [Request {RequestId}] Found {AttributionCount} attribution(s)", requestId, latestMessage.Attributions.Count());
        }

        var result = new ChatResponse(
            Response: responseText,
            Success: true,
            Error: null,
            ConversationId: conversationId,
            Attributions: latestMessage?.Attributions
        );

        logger.LogInformation("✅ [Request {RequestId}] Chat request completed successfully", requestId);
        return Results.Ok(result);
    }
    catch (HttpRequestException ex)
    {
        logger.LogError("🌐 [Request {RequestId}] M365 Copilot API Error: {Error}", requestId, ex.Message);
        logger.LogError("🔍 [Request {RequestId}] HTTP Exception Details: {Details}", requestId, ex.ToString());
        
        return Results.BadRequest(new ChatResponse(
            Response: "",
            Success: false,
            Error: $"M365 Copilot API Error: {ex.Message}",
            ConversationId: request.ConversationId,
            Attributions: null
        ));
    }
    
    catch (Exception ex)
    {
        logger.LogError("💥 [Request {RequestId}] Unexpected error: {Error}", requestId, ex.Message);
        logger.LogError("🔍 [Request {RequestId}] Exception Details: {Details}", requestId, ex.ToString());

        return Results.Problem(new ChatResponse(
            Response: "",
            Success: false,
            Error: $"Internal server error: {ex.Message}",
            ConversationId: request.ConversationId,
            Attributions: null
        ).ToString());
    }
}).RequireAuthorization();

app.MapPost("/api/similarity/score", async (ICopilotService copilotService, GraphSearchService graphSearchService, SimilarityRequest request, ILogger<Program> logger) =>
{
    var requestId = Guid.NewGuid().ToString("N")[..8];
    logger.LogInformation("📊 [Similarity {RequestId}] Starting semantic similarity evaluation using Copilot", requestId);
    logger.LogInformation("📝 [Similarity {RequestId}] Expected: '{Expected}' (Length: {ExpectedLength})", 
        requestId, 
        request.Expected?.Substring(0, Math.Min(request.Expected?.Length ?? 0, 100)) + (request.Expected?.Length > 100 ? "..." : ""),
        request.Expected?.Length ?? 0);
    logger.LogInformation("🤖 [Similarity {RequestId}] Actual: '{Actual}' (Length: {ActualLength})", 
        requestId, 
        request.Actual?.Substring(0, Math.Min(request.Actual?.Length ?? 0, 100)) + (request.Actual?.Length > 100 ? "..." : ""),
        request.Actual?.Length ?? 0);
    logger.LogInformation("🔑 [Similarity {RequestId}] Has access token: {HasToken}", requestId, !string.IsNullOrEmpty(request.AccessToken));
    logger.LogInformation("🗃️ [Similarity {RequestId}] Knowledge source: {KnowledgeSource}", requestId, request.SelectedKnowledgeSource ?? "None");
    
    try
    {
        if (string.IsNullOrEmpty(request.AccessToken))
        {
            logger.LogWarning("❌ [Similarity {RequestId}] Access token is missing", requestId);
            return Results.BadRequest(new SimilarityResponse(
                Score: 0.0,
                Success: false,
                Error: "Access token is required for semantic evaluation using Copilot API"
            ));
        }

        logger.LogInformation("🧠 [Similarity {RequestId}] Creating evaluation conversation with Copilot...", requestId);
        var startTime = DateTime.UtcNow;
        
        // Create a new conversation for evaluation
        var conversationId = await copilotService.CreateConversationAsync(request.AccessToken);
        logger.LogInformation("✅ [Similarity {RequestId}] Created evaluation conversation: {ConversationId}", requestId, conversationId);

        // Construct the evaluation prompt, embedding any additional instructions
        var evaluationPrompt = $@"You are an expert evaluator. Please compare these two responses and determine if they provide semantically equivalent answers.

Expected Response: ""{request.Expected ?? ""}""
Actual Response: ""{request.Actual ?? ""}""

Please analyze the semantic similarity and respond with EXACTLY this format (no additional text before or after):

Score: [number between 0.0 and 1.0]
Reasoning: [brief explanation of your evaluation]
Differences: [key differences, or 'None' if semantically equivalent]

Examples of correct format:
Score: 0.9
Reasoning: Both responses explain the same cooking process with minor wording differences
Differences: Expected uses 'medium heat' while actual uses 'moderate temperature'

Score: 0.3
Reasoning: Responses address different aspects of the question
Differences: Expected focuses on preparation steps, actual focuses on nutritional benefits

Scoring guide:
- 1.0 = Semantically identical (same meaning, even if different wording)
- 0.8-0.9 = Very similar meaning with minor differences
- 0.5-0.7 = Partially similar but notable differences in meaning
- 0.2-0.4 = Different meanings but some related concepts
- 0.0-0.1 = Completely different meanings

Focus on semantic meaning rather than exact word matching. Start your response with 'Score:'";

        // Embed additional instructions directly in the prompt if provided
        if (!string.IsNullOrWhiteSpace(request.AdditionalInstructions))
        {
            evaluationPrompt = $"{request.AdditionalInstructions}\n\n{evaluationPrompt}";
            logger.LogInformation("📋 [Similarity {RequestId}] Embedded additional evaluation instructions in prompt", requestId);
        }

        // Create the chat request for evaluation (no additional context needed)
        var chatRequest = new CopilotChatRequest(
            Message: new CopilotConversationRequestMessage(evaluationPrompt),
            AdditionalContext: null, // All instructions now embedded in main prompt
            LocationHint: new CopilotConversationLocation(
                Latitude: null,
                Longitude: null,
                TimeZone: "UTC",
                CountryOrRegion: null,
                CountryOrRegionConfidence: null
            )
        );

        logger.LogInformation("🌐 [Similarity {RequestId}] Sending evaluation request to Copilot...", requestId);
        
        // Send evaluation request to Copilot
        var response = await copilotService.ChatAsync(request.AccessToken, conversationId, chatRequest);
        
        var duration = DateTime.UtcNow - startTime;
        logger.LogInformation("⚡ [Similarity {RequestId}] Copilot evaluation completed in {Duration}ms", requestId, duration.TotalMilliseconds);

        // Get the evaluation response
        var latestMessage = response.Messages.LastOrDefault();
        var evaluationText = latestMessage?.Text ?? "No evaluation received";
        
        logger.LogInformation("📨 [Similarity {RequestId}] Evaluation response received - Length: {Length} chars", requestId, evaluationText.Length);
        logger.LogInformation("📋 [Similarity {RequestId}] Full evaluation: {Evaluation}", requestId, evaluationText);

        // Parse the evaluation response
        var (score, reasoning, differences) = ParseEvaluationResponse(evaluationText, logger, requestId);
        
        logger.LogInformation("📈 [Similarity {RequestId}] ✨ FINAL SIMILARITY SCORE: {Score:F4} ✨", requestId, score);
        logger.LogInformation("💭 [Similarity {RequestId}] Reasoning: {Reasoning}", requestId, reasoning);
        logger.LogInformation("🔍 [Similarity {RequestId}] Differences: {Differences}", requestId, differences);
        
        var result = new SimilarityResponse(
            Score: score,
            Success: true,
            Error: null,
            Reasoning: reasoning,
            Differences: differences
        );
        
        logger.LogInformation("✅ [Similarity {RequestId}] Semantic evaluation completed successfully with score: {Score:F4}", requestId, score);
        return Results.Ok(result);
    }
    catch (HttpRequestException ex)
    {
        logger.LogError("🌐 [Similarity {RequestId}] Copilot API Error: {Error}", requestId, ex.Message);
        logger.LogError("🔍 [Similarity {RequestId}] HTTP Exception Details: {Details}", requestId, ex.ToString());
        
        return Results.BadRequest(new SimilarityResponse(
            Score: 0.0,
            Success: false,
            Error: $"Copilot API Error: {ex.Message}"
        ));
    }
    catch (Exception ex)
    {
        logger.LogError("💥 [Similarity {RequestId}] Error during semantic evaluation: {Error}", requestId, ex.Message);
        logger.LogError("🔍 [Similarity {RequestId}] Exception Details: {Details}", requestId, ex.ToString());
        logger.LogError("🔍 [Similarity {RequestId}] Request Details - Expected: '{Expected}', Actual: '{Actual}', HasToken: {HasToken}", 
            requestId, 
            request.Expected?.Substring(0, Math.Min(request.Expected?.Length ?? 0, 50)) ?? "NULL",
            request.Actual?.Substring(0, Math.Min(request.Actual?.Length ?? 0, 50)) ?? "NULL",
            !string.IsNullOrEmpty(request.AccessToken));
        
        return Results.BadRequest(new SimilarityResponse(
            Score: 0.0,
            Success: false,
            Error: ex.Message
        ));
    }
}).RequireAuthorization();

// Temporary debug endpoint to manually trigger job processing
app.MapPost("/api/debug/process-job/{jobId}", async (string jobId, IJobRepository jobRepository, IExecutionService executionService, ILogger<Program> logger) =>
{
    var requestId = Guid.NewGuid().ToString("N")[..8];
    logger.LogInformation("🔧 [Debug {RequestId}] Manually triggering job processing for: {JobId}", requestId, jobId);
    
    try
    {
        // Get the job from database
        var jobEntity = await jobRepository.GetJobByIdAsync(jobId);
        if (jobEntity == null)
        {
            logger.LogError("❌ [Debug {RequestId}] Job not found: {JobId}", requestId, jobId);
            return Results.NotFound(new { error = $"Job {jobId} not found" });
        }
        
        var job = jobEntity.ToJob();
        logger.LogInformation("✅ [Debug {RequestId}] Job found, starting execution: {JobName}", requestId, job.Name);
        
        // Execute the job directly
        var result = await executionService.ExecuteJobAsync(job);
        
        if (result.Success)
        {
            // Update job status to completed
            jobEntity.Status = JobStatus.Completed;
            jobEntity.CompletedAt = DateTimeOffset.UtcNow;
            
            if (result.ArtifactBlobReference != null)
            {
                jobEntity.ResultsBlobReferenceJson = System.Text.Json.JsonSerializer.Serialize(result.ArtifactBlobReference);
            }
            
            await jobRepository.UpdateJobAsync(jobEntity);
            
            logger.LogInformation("✅ [Debug {RequestId}] Job completed successfully: {JobId}", requestId, jobId);
            return Results.Ok(new { 
                message = "Job processed successfully", 
                jobId = jobId,
                status = "completed",
                hasResults = result.ArtifactBlobReference != null,
                resultsSummary = result.Results?.Summary
            });
        }
        else
        {
            logger.LogError("❌ [Debug {RequestId}] Job execution failed: {JobId}", requestId, jobId);
            return Results.BadRequest(new { 
                error = "Job execution failed", 
                details = result.ErrorDetails 
            });
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "💥 [Debug {RequestId}] Error processing job: {JobId}", requestId, jobId);
        return Results.Problem(new { error = ex.Message }.ToString());
    }
}).RequireAuthorization();

app.Run();

logger.LogInformation("🛑 Copilot Evaluation API shutting down...");

// Helper method to parse Copilot's evaluation response
static (double score, string reasoning, string differences) ParseEvaluationResponse(string evaluationText, ILogger logger, string requestId)
{
    try
    {
        logger.LogInformation("🔍 [Similarity {RequestId}] Parsing evaluation response...", requestId);
        
        var lines = evaluationText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        double score = 0.0;
        string reasoning = "Unable to parse reasoning";
        string differences = "Unable to parse differences";

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // More flexible parsing for score
            if (trimmedLine.StartsWith("Score:", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("Similarity:", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("Rating:", StringComparison.OrdinalIgnoreCase))
            {
                var colonIndex = trimmedLine.IndexOf(':');
                if (colonIndex > 0)
                {
                    var scoreText = trimmedLine.Substring(colonIndex + 1).Trim();
                    
                    // Try to extract number from various formats like "0.8", "8/10", "80%", etc.
                    var regex = new System.Text.RegularExpressions.Regex(@"(\d+\.?\d*)");
                    var match = regex.Match(scoreText);
                    
                    if (match.Success && double.TryParse(match.Value, out var parsedScore))
                    {
                        // Handle different scales (0-1, 0-10, 0-100)
                        if (parsedScore > 1.0 && parsedScore <= 10.0)
                            score = parsedScore / 10.0; // Convert 0-10 scale to 0-1
                        else if (parsedScore > 10.0)
                            score = parsedScore / 100.0; // Convert 0-100 scale to 0-1
                        else
                            score = parsedScore; // Already 0-1 scale
                            
                        score = Math.Max(0.0, Math.Min(1.0, score)); // Clamp between 0 and 1
                        logger.LogInformation("✅ [Similarity {RequestId}] Parsed score: {Score} from text: '{ScoreText}'", requestId, score, scoreText);
                    }
                    else
                    {
                        logger.LogWarning("⚠️ [Similarity {RequestId}] Could not parse score from: '{ScoreText}'", requestId, scoreText);
                    }
                }
            }
            else if (trimmedLine.StartsWith("Reasoning:", StringComparison.OrdinalIgnoreCase) ||
                     trimmedLine.StartsWith("Explanation:", StringComparison.OrdinalIgnoreCase) ||
                     trimmedLine.StartsWith("Analysis:", StringComparison.OrdinalIgnoreCase))
            {
                var colonIndex = trimmedLine.IndexOf(':');
                if (colonIndex > 0)
                {
                    reasoning = trimmedLine.Substring(colonIndex + 1).Trim();
                    logger.LogInformation("✅ [Similarity {RequestId}] Parsed reasoning: '{Reasoning}'", requestId, reasoning.Substring(0, Math.Min(reasoning.Length, 100)) + (reasoning.Length > 100 ? "..." : ""));
                }
            }
            else if (trimmedLine.StartsWith("Differences:", StringComparison.OrdinalIgnoreCase) ||
                     trimmedLine.StartsWith("Key differences:", StringComparison.OrdinalIgnoreCase))
            {
                var colonIndex = trimmedLine.IndexOf(':');
                if (colonIndex > 0)
                {
                    differences = trimmedLine.Substring(colonIndex + 1).Trim();
                    logger.LogInformation("✅ [Similarity {RequestId}] Parsed differences: '{Differences}'", requestId, differences.Substring(0, Math.Min(differences.Length, 100)) + (differences.Length > 100 ? "..." : ""));
                }
            }
        }

        // If we couldn't parse the score from structured format, try more aggressive parsing
        if (score == 0.0)
        {
            logger.LogWarning("⚠️ [Similarity {RequestId}] Could not find structured score, attempting aggressive parsing", requestId);
            
            // Look for patterns like "0.8", "8/10", "80%", "8 out of 10", etc.
            var patterns = new[]
            {
                @"(\d+\.?\d*)/10",           // "8/10" or "8.5/10"
                @"(\d+\.?\d*)%",             // "80%" or "85.5%"
                @"(\d+\.?\d*)\s*out\s*of\s*10", // "8 out of 10"
                @"(\d+\.?\d*)\s*\/\s*1\.?0?", // "0.8/1" or "8/10"
                @"(\d+\.?\d*)"               // Any decimal number as fallback
            };
            
            foreach (var pattern in patterns)
            {
                var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var match = regex.Match(evaluationText);
                
                if (match.Success && double.TryParse(match.Groups[1].Value, out var fallbackScore))
                {
                    // Apply appropriate scaling based on the pattern matched
                    if (pattern.Contains("/10") || pattern.Contains("out of 10"))
                        score = fallbackScore / 10.0;
                    else if (pattern.Contains("%"))
                        score = fallbackScore / 100.0;
                    else if (fallbackScore > 1.0 && fallbackScore <= 10.0)
                        score = fallbackScore / 10.0;
                    else if (fallbackScore > 10.0)
                        score = fallbackScore / 100.0;
                    else
                        score = fallbackScore;
                        
                    score = Math.Max(0.0, Math.Min(1.0, score));
                    logger.LogInformation("🔄 [Similarity {RequestId}] Fallback score extracted: {Score} using pattern: {Pattern}", requestId, score, pattern);
                    break;
                }
            }
            
            // If still no score, use the full response as reasoning and set a neutral score
            if (score == 0.0)
            {
                score = 0.5;
                reasoning = $"Could not parse numerical score. Full response: {evaluationText.Substring(0, Math.Min(evaluationText.Length, 300))}...";
                logger.LogWarning("⚠️ [Similarity {RequestId}] All parsing failed, using default score of 0.5", requestId);
            }
        }

        return (score, reasoning, differences);
    }
    catch (Exception ex)
    {
        logger.LogError("💥 [Similarity {RequestId}] Error parsing evaluation response: {Error}", requestId, ex.Message);
        return (0.5, $"Error parsing response: {ex.Message}", "Unable to determine differences due to parsing error");
    }
}

