using Spot.Auth.Api.DTOs;

namespace Spot.Auth.Api.Services;

public interface IAuthService
{
    /// <exception cref="Repositories.DuplicateEmailException">The email is already registered.</exception>
    Task<AuthResponseDto> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Validates <paramref name="request"/>'s credentials and, if valid, issues a new session
    /// (access + refresh token). Returns null for ANY invalid-credentials reason (unknown email,
    /// no password set on the account, wrong password) — the caller must turn that into the same
    /// 401 response every time, never revealing which reason it was.
    /// </summary>
    Task<AuthResponseDto?> LoginAsync(LoginRequest request, CancellationToken ct = default);
}
