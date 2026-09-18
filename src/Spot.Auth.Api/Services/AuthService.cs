using Microsoft.AspNetCore.Identity;
using Spot.Auth.Api.DTOs;
using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;

namespace Spot.Auth.Api.Services;

public sealed class AuthService(
    IUserRepository userRepository,
    IPasswordHasher<User> passwordHasher,
    ITokenService tokenService,
    IRefreshTokenIssuer refreshTokenIssuer,
    IGoogleIdTokenValidator googleIdTokenValidator) : IAuthService
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

        var issuedRefreshToken = refreshTokenIssuer.Issue();
        var refreshToken = new RefreshToken
        {
            TokenHash = issuedRefreshToken.HashValue,
            ExpiresAt = issuedRefreshToken.ExpiresAt,
        };

        await userRepository.CreateAsync(user, refreshToken, ct);

        return BuildAuthResponse(user, issuedRefreshToken.RawValue);
    }

    public async Task<AuthResponseDto?> LoginWithGoogleAsync(GoogleLoginRequest request, CancellationToken ct = default)
    {
        var identity = await googleIdTokenValidator.ValidateAsync(request.IdToken, ct);
        if (identity is null)
            return null;

        // Returning Google user: already linked, nothing to create.
        var user = await userRepository.GetByProviderAsync(AuthProvider.GOOGLE, identity.Subject, ct);
        if (user is not null)
            return await IssueForExistingUserAsync(user, ct);

        // First time signing in with Google, but an email/password account with the same email
        // already exists: link this Google identity to it instead of creating a duplicate user.
        // Safe only because IGoogleIdTokenValidator already guaranteed the email is verified.
        var normalizedEmail = NormalizeEmail(identity.Email);
        user = await userRepository.GetByEmailAsync(normalizedEmail, ct);
        if (user is not null)
        {
            // Added to the tracked collection now, before IssueForExistingUserAsync's single
            // SaveChangesAsync call below — so the new provider link and the new refresh token
            // are persisted together, atomically.
            user.AuthProviders.Add(new UserAuthProvider { Provider = AuthProvider.GOOGLE, ProviderUserId = identity.Subject });
            return await IssueForExistingUserAsync(user, ct);
        }

        // Brand-new user: no password, created together with the Google link and first refresh
        // token in a single CreateAsync call, same as RegisterAsync above.
        var newUser = new User
        {
            Email = normalizedEmail,
            // given_name/family_name are present on virtually every real Google ID token, but
            // aren't contractually guaranteed — users.first_name/last_name are NOT NULL, so a
            // missing one falls back to something non-empty rather than letting the insert fail.
            FirstName = string.IsNullOrWhiteSpace(identity.GivenName) ? normalizedEmail.Split('@')[0] : identity.GivenName.Trim(),
            LastName = string.IsNullOrWhiteSpace(identity.FamilyName) ? "Google" : identity.FamilyName.Trim(),
            ProfilePhotoUrl = identity.Picture,
            Role = UserRole.CLIENT,
        };
        newUser.AuthProviders.Add(new UserAuthProvider { Provider = AuthProvider.GOOGLE, ProviderUserId = identity.Subject });

        var issuedRefreshToken = refreshTokenIssuer.Issue();
        var refreshToken = new RefreshToken
        {
            TokenHash = issuedRefreshToken.HashValue,
            ExpiresAt = issuedRefreshToken.ExpiresAt,
        };
        await userRepository.CreateAsync(newUser, refreshToken, ct);

        return BuildAuthResponse(newUser, issuedRefreshToken.RawValue);
    }

    /// <summary>
    /// Issues and persists a new refresh token for an already-existing, already-loaded
    /// <paramref name="user"/> — via the same tracked-navigation + SaveChangesAsync pattern
    /// <see cref="Repositories.UserRepository.CreateAsync"/> uses for a brand-new one, just
    /// against a user that's already in the database.
    /// </summary>
    private async Task<AuthResponseDto> IssueForExistingUserAsync(User user, CancellationToken ct)
    {
        var rawRefreshToken = IssueRawRefreshTokenOnto(user);
        await userRepository.SaveChangesAsync(user, ct);
        return BuildAuthResponse(user, rawRefreshToken);
    }

    private string IssueRawRefreshTokenOnto(User user)
    {
        var issuedRefreshToken = refreshTokenIssuer.Issue();
        user.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = issuedRefreshToken.HashValue,
            ExpiresAt = issuedRefreshToken.ExpiresAt,
        });
        return issuedRefreshToken.RawValue;
    }

    /// <summary>
    /// Issues the access token and assembles the response shared by every auth flow (register,
    /// Google) — the one piece of token-issuing logic every caller must go through, so it can
    /// never drift between flows.
    /// </summary>
    private AuthResponseDto BuildAuthResponse(User user, string rawRefreshToken)
    {
        var accessToken = tokenService.IssueAccessToken(user.Id.ToString(), user.Role.ToString());

        return new AuthResponseDto(
            UserDto.FromEntity(user),
            new AuthTokensDto(accessToken.Value, rawRefreshToken, "Bearer", accessToken.ExpiresInSeconds));
    }

    /// <summary>
    /// Lower-cases and trims the email so "Foo@Example.com" and "foo@example.com" are treated as
    /// the same account — the database's unique index on email is otherwise case-sensitive.
    /// </summary>
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
