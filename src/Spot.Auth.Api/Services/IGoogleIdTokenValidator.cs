namespace Spot.Auth.Api.Services;

/// <summary>
/// Validates a Google-issued ID token (signature, issuer, audience, expiry) and extracts the
/// caller's identity from it. Wrapped behind an interface — like <see cref="ITokenService"/> and
/// <see cref="IRefreshTokenIssuer"/> — so <c>AuthService</c> stays unit-testable without a real
/// Google ID token.
/// </summary>
public interface IGoogleIdTokenValidator
{
    /// <summary>
    /// Returns the validated identity, or null if the token is malformed, expired, has an
    /// invalid signature, targets an audience outside <c>GoogleAuth:AllowedAudiences</c>, or has
    /// an unverified email (see <see cref="GoogleIdentity"/> remarks).
    /// </summary>
    Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct = default);
}

/// <summary>
/// The subset of a validated Google ID token's claims this service needs.
/// </summary>
/// <param name="Subject">The <c>sub</c> claim — Google's stable, unique id for this account.</param>
/// <remarks>
/// <paramref name="Email"/> is only ever populated here when Google's own <c>email_verified</c>
/// claim was true — an unverified email must never be trusted to link this identity to an
/// existing password account, or a token holder could claim someone else's account by presenting
/// an address they don't actually control.
/// </remarks>
public sealed record GoogleIdentity(string Subject, string Email, string? GivenName, string? FamilyName, string? Picture);
