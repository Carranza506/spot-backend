namespace Spot.Auth.Api.DTOs;

/// <summary>Matches the AuthResponse schema in contracts/spot-api.yaml.</summary>
public sealed record AuthResponseDto(UserDto User, AuthTokensDto Tokens);
