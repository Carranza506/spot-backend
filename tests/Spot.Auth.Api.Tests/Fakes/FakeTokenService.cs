using Spot.Auth.Api.Services;

namespace Spot.Auth.Api.Tests.Fakes;

/// <summary>Records the last call instead of signing a real JWT — keeps service-level tests
/// focused on AuthService's own logic rather than JwtTokenService's (already covered
/// separately by JwtTokenServiceTests).</summary>
public sealed class FakeTokenService : ITokenService
{
    public string? LastUserId { get; private set; }
    public string? LastRole { get; private set; }

    public AccessToken IssueAccessToken(string userId, string role)
    {
        LastUserId = userId;
        LastRole = role;
        return new AccessToken("fake-access-token", 3600);
    }
}
