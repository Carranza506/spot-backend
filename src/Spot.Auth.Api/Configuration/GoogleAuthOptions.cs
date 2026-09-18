namespace Spot.Auth.Api.Configuration;

/// <summary>
/// Configuration for validating Google ID tokens (POST /auth/google).
/// </summary>
/// <remarks>
/// Only the web client audience is populated for now — Android/iOS (#95, #96) can be wired up
/// later purely via config, by appending their client ids to <see cref="AllowedAudiences"/>, with
/// no code change.
/// </remarks>
public sealed class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    /// <summary>
    /// Google OAuth client ids this service accepts as the <c>aud</c> claim of an incoming ID
    /// token — one per platform (web, Android, iOS). Not secret: these are the same client ids
    /// embedded in the corresponding frontend/app.
    /// </summary>
    public List<string> AllowedAudiences { get; set; } = [];
}
