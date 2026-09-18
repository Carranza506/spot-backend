using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using Spot.Auth.Api.Configuration;

namespace Spot.Auth.Api.Services;

/// <summary>
/// Validates Google ID tokens via Google's own client library, restricted to the audiences
/// configured in <see cref="GoogleAuthOptions"/>.
/// </summary>
public sealed class GoogleIdTokenValidator(IOptions<GoogleAuthOptions> options) : IGoogleIdTokenValidator
{
    public async Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct = default)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            // GoogleJsonWebSignature.ValidateAsync itself checks signature, issuer, expiry, and
            // (via Audience below) that the token was actually issued for this app — not
            // something obtained by another Google client and replayed here.
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = options.Value.AllowedAudiences,
            });
        }
        catch (InvalidJwtException)
        {
            return null;
        }

        // See GoogleIdentity's remarks: an unverified email must never be trusted for account
        // linking, so this is treated the same as any other validation failure.
        if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email))
            return null;

        return new GoogleIdentity(payload.Subject, payload.Email, payload.GivenName, payload.FamilyName, payload.Picture);
    }
}
