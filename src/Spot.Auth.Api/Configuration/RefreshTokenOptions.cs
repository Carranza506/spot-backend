namespace Spot.Auth.Api.Configuration;

/// <summary>Configuration for refresh tokens issued alongside an access token.</summary>
public sealed class RefreshTokenOptions
{
    public const string SectionName = "RefreshToken";

    /// <summary>How long a refresh token stays valid after issuance.</summary>
    public int ExpirationDays { get; set; } = 30;
}
