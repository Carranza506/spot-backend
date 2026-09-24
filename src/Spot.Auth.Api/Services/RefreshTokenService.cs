using Spot.Auth.Api.DTOs;
using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;

namespace Spot.Auth.Api.Services;

public class RefreshTokenService(
    IRefreshTokenRepository repository,
    IRefreshTokenHasher hasher,
    IUserRepository userRepository,
    ITokenService tokenService,
    IRefreshTokenIssuer refreshTokenIssuer) : IRefreshTokenService
{
    public async Task RevokeAsync(Guid userId, string rawRefreshToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawRefreshToken);

        var tokenHash = hasher.Hash(rawRefreshToken);
        var token = await repository.FindActiveByHashAsync(userId, tokenHash, ct);

        // No matching active token for this user: nothing to revoke. Not an error — see
        // IRefreshTokenService.RevokeAsync's remarks on why this stays silent either way.
        if (token is null)
            return;

        await repository.RevokeAsync(token, ct);
    }

    public async Task<AuthTokensDto?> RefreshAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawRefreshToken);

        var tokenHash = hasher.Hash(rawRefreshToken);
        var token = await repository.FindActiveByHashAsync(tokenHash, ct);
        if (token is null)
            return null;

        var user = await userRepository.GetByIdAsync(token.UserId, ct);

        // The token names a user that no longer exists (e.g. deleted after issuing it) — treat
        // it the same as "not a valid session" rather than issuing a token for a nonexistent user.
        if (user is null)
            return null;

        // Revoke before issuing the replacement: the consumed token is rotated, not reusable,
        // so a stolen copy of it can't be replayed for a second token pair. The revoke is
        // atomic/conditional at the database level, so if a concurrent request for this same
        // token already revoked it between our read above and this write, we lose the race and
        // must not hand out a second pair for a single-use token.
        var revoked = await repository.RevokeAsync(token, ct);
        if (!revoked)
            return null;

        // Reuses IRefreshTokenIssuer — the same one AuthService.RegisterAsync uses — so issuing
        // a refresh token can never silently drift between the two call sites (entropy, hashing,
        // lifetime).
        var issued = refreshTokenIssuer.Issue();
        var newToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = issued.HashValue,
            ExpiresAt = issued.ExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await repository.CreateAsync(newToken, ct);

        var accessToken = tokenService.IssueAccessToken(user.Id.ToString(), user.Role.ToString());

        return new AuthTokensDto(
            AccessToken: accessToken.Value,
            RefreshToken: issued.RawValue,
            TokenType: "Bearer",
            ExpiresIn: accessToken.ExpiresInSeconds);
    }

    public Task RevokeAllActiveForUserAsync(Guid userId, CancellationToken ct = default) =>
        repository.RevokeAllActiveForUserAsync(userId, ct);
}
