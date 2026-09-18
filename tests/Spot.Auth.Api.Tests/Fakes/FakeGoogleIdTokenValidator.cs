using Spot.Auth.Api.Services;

namespace Spot.Auth.Api.Tests.Fakes;

/// <summary>Returns a canned identity instead of calling Google — keeps AuthService tests
/// focused on its own logic rather than GoogleIdTokenValidator's (a thin wrapper around
/// GoogleJsonWebSignature, not worth re-testing here).</summary>
public sealed class FakeGoogleIdTokenValidator : IGoogleIdTokenValidator
{
    public GoogleIdentity? IdentityToReturn { get; set; }
    public string? LastIdToken { get; private set; }

    public Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct = default)
    {
        LastIdToken = idToken;
        return Task.FromResult(IdentityToReturn);
    }
}
