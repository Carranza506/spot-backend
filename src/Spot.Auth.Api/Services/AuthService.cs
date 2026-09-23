using Microsoft.AspNetCore.Identity;
using Spot.Auth.Api.DTOs;
using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;

namespace Spot.Auth.Api.Services;

public sealed class AuthService(
    IUserRepository userRepository,
    IPasswordHasher<User> passwordHasher,
    ITokenService tokenService,
    IRefreshTokenIssuer refreshTokenIssuer) : IAuthService
{
    public async Task<AuthResponseDto> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var role = request.Role ?? UserRole.CLIENT;

        var user = new User
        {
            Email = NormalizeEmail(request.Email),
            // A BUSINESS account is the business itself — it never has a person name, regardless
            // of what the request sent (RegisterRequest.Validate only requires these for CLIENT;
            // it never forbids them for BUSINESS, so this is what actually enforces "null names").
            FirstName = role == UserRole.CLIENT ? request.FirstName!.Trim() : null,
            LastName = role == UserRole.CLIENT ? request.LastName!.Trim() : null,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Role = role,
        };
        // Hashed before the entity is ever tracked/persisted — the plain-text password never
        // reaches the repository or the database.
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        var issuedRefreshToken = refreshTokenIssuer.Issue();
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

    public async Task<AuthResponseDto?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userRepository.GetByEmailAsync(NormalizeEmail(request.Email), ct);

        // No such user, or an account with no password set (e.g. Google-only sign-up, once
        // social login exists) — either way this returns null exactly like a wrong password
        // below, so the controller answers with the same 401 no matter which one happened.
        if (user is null || user.PasswordHash is null)
            return null;

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification is not (PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded))
            return null;

        var issuedRefreshToken = refreshTokenIssuer.Issue();
        var refreshToken = new RefreshToken
        {
            TokenHash = issuedRefreshToken.HashValue,
            ExpiresAt = issuedRefreshToken.ExpiresAt,
        };
        await userRepository.AddRefreshTokenAsync(user, refreshToken, ct);

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
