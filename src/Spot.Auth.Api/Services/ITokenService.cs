namespace Spot.Auth.Api.Services;

/// <summary>
/// Issues signed access tokens for authenticated users. This is the only piece a future
/// login/register/refresh flow needs to depend on — it has no knowledge of credentials,
/// persistence, or HTTP.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Issues a signed RS256 access token for the given user.
    /// </summary>
    /// <param name="userId">The user's id, emitted as the <c>sub</c> claim.</param>
    /// <param name="role">The user's role (e.g. CLIENT, BUSINESS_OWNER, SUPERADMIN), emitted as the <c>role</c> claim.</param>
    AccessToken IssueAccessToken(string userId, string role);
}

/// <summary>
/// A freshly issued access token, shaped to map 1:1 onto the <c>accessToken</c>/<c>expiresIn</c>
/// fields of the <c>AuthTokens</c> schema in contracts/spot-api.yaml.
/// </summary>
/// <param name="Value">The encoded JWT.</param>
/// <param name="ExpiresInSeconds">Seconds until the token expires.</param>
public sealed record AccessToken(string Value, int ExpiresInSeconds);
