namespace Spot.Auth.Api.Configuration;

/// <summary>
/// Configuration for refresh token issuing. Local to Spot.Auth.Api — unlike <c>JwtOptions</c>
/// (Spot.Shared), no other microservice needs to know a refresh token's lifetime, since only
/// Spot.Auth.Api ever issues, validates, or revokes them.
/// </summary>
public sealed class RefreshTokenOptions
{
    /// <summary>The configuration section name this type binds to.</summary>
    public const string SectionName = "RefreshToken";

    /// <summary>Refresh token lifetime in days.</summary>
    public int ExpiresInDays { get; set; } = 30;
}
