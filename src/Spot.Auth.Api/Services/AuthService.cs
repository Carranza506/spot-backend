using Microsoft.AspNetCore.Identity;
using Spot.Auth.Api.DTOs;
using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;

namespace Spot.Auth.Api.Services;

public sealed class AuthService(
    IUserRepository userRepository,
    IPasswordHasher<User> passwordHasher,
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService) : IAuthService
{
    public async Task<AuthResponseDto> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var user = new User
        {
            Email = NormalizeEmail(request.Email),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Role = UserRole.CLIENT,
        };
        // Hashed before the entity is ever tracked/persisted — the plain-text password never
        // reaches the repository or the database.
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        var issuedRefreshToken = refreshTokenService.Issue();
        var refreshToken = new RefreshToken
        {
            TokenHash = issuedRefreshToken.HashValue,
            ExpiresAt = issuedRefreshToken.ExpiresAt,
        };

        await userRepository.CreateAsync(user, refreshToken, ct);

        var accessToken = tokenService.IssueAccessToken(user.Id.ToString(), user.Role.ToString());

        return new AuthResponseDto(
            UserDto.FromEntity(user),
            new AuthTokensDto(accessToken.Value, issuedRefreshToken.RawValue, "Bearer", accessToken.ExpiresInSeconds));
    }

    /// <summary>
    /// Lower-cases and trims the email so "Foo@Example.com" and "foo@example.com" are treated as
    /// the same account — the database's unique index on email is otherwise case-sensitive.
    /// </summary>
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
