using Spot.Auth.Api.DTOs;

namespace Spot.Auth.Api.Services;

public interface IAuthService
{
    /// <exception cref="Repositories.DuplicateEmailException">The email is already registered.</exception>
    Task<AuthResponseDto> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Logs in via Google, creating the account (and linking it to any existing password
    /// account with the same email) on first sign-in. Returns null if <paramref name="request"/>'s
    /// id token doesn't validate — see <see cref="IGoogleIdTokenValidator"/>.
    /// </summary>
    Task<AuthResponseDto?> LoginWithGoogleAsync(GoogleLoginRequest request, CancellationToken ct = default);
}
