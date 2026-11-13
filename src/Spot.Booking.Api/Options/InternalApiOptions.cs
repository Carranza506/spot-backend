namespace Spot.Booking.Api.Options;

/// <summary>
/// "InternalApi" configuration section (#79): how to reach the Gateway's internal port. From
/// launchSettings.json for `dotnet run`, from docker-compose.yml otherwise (the key comes from the
/// internal_api_key Docker secret, see docker/entrypoint-jwt.sh). Not validated at startup: status
/// notifications are best-effort, so a missing value only logs a warning on each transition.
/// </summary>
public sealed class InternalApiOptions
{
    public const string SectionName = "InternalApi";

    /// <summary>e.g. http://gateway:8081</summary>
    public string GatewayBaseUrl { get; set; } = "";

    public string Key { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 3;
}
