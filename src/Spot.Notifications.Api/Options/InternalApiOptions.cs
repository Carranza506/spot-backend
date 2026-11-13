namespace Spot.Notifications.Api.Options;

/// <summary>
/// "InternalApi" configuration section (#79): the key service-to-service callers send in
/// X-Internal-Api-Key. From launchSettings.json for `dotnet run`, from the internal_api_key Docker
/// secret otherwise (see docker/entrypoint-jwt.sh). Not validated at startup, same as FcmOptions:
/// without a key the service still boots and every internal call answers 401.
/// </summary>
public sealed class InternalApiOptions
{
    public const string SectionName = "InternalApi";

    public string Key { get; set; } = "";
}
