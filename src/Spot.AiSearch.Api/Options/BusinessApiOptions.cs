namespace Spot.AiSearch.Api.Options;

/// <summary>
/// Where Spot.Business.Api is reachable from this service, bound from the <c>BusinessApi</c>
/// configuration section. POST /ai-search/query delegates the actual business search to it
/// (issue #73): the AI only extracts criteria and never queries business data itself.
/// </summary>
public sealed class BusinessApiOptions
{
    public const string SectionName = "BusinessApi";

    /// <summary>Base URL of Spot.Business.Api itself (not the Gateway), e.g. http://business-api:8080.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 5;
}
