namespace Spot.Auth.Api.DTOs;

/// <summary>Matches the AuthTokens schema in contracts/spot-api.yaml.</summary>
public sealed record AuthTokensDto(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn);
