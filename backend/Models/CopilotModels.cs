using System.Text.Json.Serialization;

namespace CopilotEvalApi.Models;

// M365 Copilot API Models
public record CopilotChatApiRequest(
    [property: JsonPropertyName("request")] CopilotChatRequest Request
);

public record CopilotConversationCreateResponse(
    string Id,
    DateTimeOffset CreatedDateTime,
    string DisplayName,
    string State,
    int TurnCount,
    List<CopilotConversationResponseMessage> Messages
);

public record CopilotChatRequest(
    [property: JsonPropertyName("message")] CopilotConversationRequestMessage Message,
    [property: JsonPropertyName("additionalContext")] List<CopilotContextMessage>? AdditionalContext,
    [property: JsonPropertyName("locationHint")] CopilotConversationLocation LocationHint
);

public record CopilotConversationRequestMessage(
    [property: JsonPropertyName("text")] string Text
);

public record CopilotContextMessage(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("description")] string? Description
);

public record CopilotConversationLocation(
    [property: JsonPropertyName("latitude")] float? Latitude,
    [property: JsonPropertyName("longitude")] float? Longitude,
    [property: JsonPropertyName("timeZone")] string TimeZone,
    [property: JsonPropertyName("countryOrRegion")] string? CountryOrRegion,
    [property: JsonPropertyName("countryOrRegionConfidence")] float? CountryOrRegionConfidence
);

public record CopilotConversation(
    string Id,
    DateTimeOffset CreatedDateTime,
    string DisplayName,
    string State,
    int TurnCount,
    List<CopilotConversationResponseMessage> Messages
);

public record CopilotConversationResponseMessage(
    string Id,
    string Text,
    DateTimeOffset CreatedDateTime,
    List<object>? AdaptiveCards,
    List<CopilotConversationAttribution>? Attributions,
    SearchSensitivityLabelInfo? SensitivityLabel
);

public record CopilotConversationAttribution(
    string AttributionType,
    string ProviderDisplayName,
    string AttributionSource,
    string SeeMoreWebUrl,
    string? ImageWebUrl,
    string? ImageFavIcon,
    int ImageWidth,
    int ImageHeight
);

public record SearchSensitivityLabelInfo(
    string? SensitivityLabelId,
    string? DisplayName,
    string? Tooltip,
    int? Priority,
    string? Color,
    bool? IsEncrypted
);

// OAuth and Authentication Models
public record AuthUrlResponse(
    string AuthUrl,
    string State
);

public record TokenRequest(
    string Code,
    string State,
    string? RedirectUri = null
);

public record TokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string? RefreshToken
);

// Enhanced Chat Request/Response for our API
public record ChatRequest(
    string Prompt,
    string? ConversationId,
    string? AccessToken,
    string? TimeZone,
    string? SelectedAgentId,
    string? AdditionalInstructions,
    string? KnowledgeSourceGuidance,
    string? SelectedKnowledgeSource
);

public record ChatResponse(
    string Response,
    bool Success,
    string? Error,
    string? ConversationId,
    List<CopilotConversationAttribution>? Attributions
);

// Configuration Models
public record AzureAdOptions
{
    public string Instance { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string CallbackPath { get; init; } = string.Empty;
}

public record MicrosoftGraphOptions
{
    public string BaseUrl { get; init; } = string.Empty;
    public List<string> Scopes { get; init; } = new();
}

// Teams Apps/Agents Models
public record TeamsAppsResponse(
    [property: JsonPropertyName("value")] List<TeamsApp> Value
);

public record TeamsApp(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("externalId")] string? ExternalId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("distributionMethod")] string? DistributionMethod,
    [property: JsonPropertyName("shortDescription")] string? ShortDescription,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("packageName")] string? PackageName,
    [property: JsonPropertyName("isBlocked")] bool? IsBlocked,
    [property: JsonPropertyName("publishingState")] string? PublishingState,
    [property: JsonPropertyName("teamsAppDefinition")] TeamsAppDefinition? TeamsAppDefinition
);

public record TeamsAppDefinition(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("teamsAppId")] string? TeamsAppId,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("publishingState")] string? PublishingState,
    [property: JsonPropertyName("shortDescription")] string? ShortDescription,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("lastModifiedDateTime")] DateTimeOffset? LastModifiedDateTime,
    [property: JsonPropertyName("createdBy")] UserInfo? CreatedBy,
    [property: JsonPropertyName("bot")] BotInfo? Bot
);

public record UserInfo(
    [property: JsonPropertyName("application")] ApplicationInfo? Application,
    [property: JsonPropertyName("user")] BasicUserInfo? User
);

public record ApplicationInfo(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("displayName")] string? DisplayName
);

public record BasicUserInfo(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("userIdentityType")] string? UserIdentityType
);

public record BotInfo(
    [property: JsonPropertyName("id")] string? Id
);

// Microsoft Graph Search Models for External Connections
public record ExternalConnectionsResponse(
    [property: JsonPropertyName("value")] List<ExternalConnection> Value
);

public record ExternalConnection(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("configuration")] ExternalConnectionConfiguration? Configuration
);

public record ExternalConnectionConfiguration(
    [property: JsonPropertyName("authorizedAppIds")] List<string>? AuthorizedAppIds
);

// Microsoft Graph Search Request/Response Models
public record GraphSearchRequest(
    [property: JsonPropertyName("requests")] List<SearchRequestItem> Requests
);

public record SearchRequestItem(
    [property: JsonPropertyName("entityTypes")] List<string> EntityTypes,
    [property: JsonPropertyName("contentSources")] List<string>? ContentSources,
    [property: JsonPropertyName("query")] SearchQuery Query,
    [property: JsonPropertyName("from")] int From,
    [property: JsonPropertyName("size")] int Size,
    [property: JsonPropertyName("fields")] List<string>? Fields
);

public record SearchQuery(
    [property: JsonPropertyName("queryString")] string QueryString
);

public record GraphSearchResponse(
    [property: JsonPropertyName("value")] List<SearchResponseContainer> Value
);

public record SearchResponseContainer(
    [property: JsonPropertyName("searchTerms")] List<string>? SearchTerms,
    [property: JsonPropertyName("hitsContainers")] List<SearchHitsContainer> HitsContainers
);

public record SearchHitsContainer(
    [property: JsonPropertyName("hits")] List<SearchHit> Hits,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("moreResultsAvailable")] bool MoreResultsAvailable
);

public record SearchHit(
    [property: JsonPropertyName("hitId")] string HitId,
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("resource")] object? Resource
);

// Enhanced models with knowledge source support
public record SimilarityRequest(
    string Expected,
    string Actual,
    string? AccessToken,
    string? AdditionalInstructions,
    string? SelectedKnowledgeSource
);

public record SimilarityResponse(
    double Score, 
    bool Success, 
    string? Error = null, 
    string? Reasoning = null, 
    string? Differences = null
);
