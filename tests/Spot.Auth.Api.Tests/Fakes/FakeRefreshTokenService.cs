using Spot.Auth.Api.DTOs;
using Spot.Auth.Api.Services;

namespace Spot.Auth.Api.Tests.Fakes;

/// <summary>In-memory stand-in for IRefreshTokenService — records how it was called instead of
/// touching a database. RefreshTokenService's own revoke/refresh logic is covered separately by
/// RefreshTokenServiceTests against a real (InMemory) repository.</summary>
public sealed class FakeRefreshTokenService : IRefreshTokenService
{
    public bool WasCalled { get; private set; }
    public Guid? LastCalledWithUserId { get; private set; }
    public string? LastCalledWithRawToken { get; private set; }
    public AuthTokensDto? TokensToReturn { get; set; }
    public string? LastRefreshCalledWithRawToken { get; private set; }
    public Guid? LastRevokeAllForUserId { get; private set; }

    public Task RevokeAsync(Guid userId, string rawRefreshToken, CancellationToken ct = default)
    {
        WasCalled = true;
        LastCalledWithUserId = userId;
        LastCalledWithRawToken = rawRefreshToken;
        return Task.CompletedTask;
    }

    public Task<AuthTokensDto?> RefreshAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        LastRefreshCalledWithRawToken = rawRefreshToken;
        return Task.FromResult(TokensToReturn);
    }

    public Task RevokeAllActiveForUserAsync(Guid userId, CancellationToken ct = default)
    {
        LastRevokeAllForUserId = userId;
        return Task.CompletedTask;
    }

    public void Reset()
    {
        WasCalled = false;
        LastCalledWithUserId = null;
        LastCalledWithRawToken = null;
        TokensToReturn = null;
        LastRefreshCalledWithRawToken = null;
        LastRevokeAllForUserId = null;
    }
}
