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

    /// <summary>
    /// Changes <paramref name="userId"/>'s password after verifying <paramref name="request"/>'s
    /// current password, then revokes every one of their active refresh tokens (forcing every
    /// other session/device to log in again). Returns false if no such user exists.
    /// </summary>
    /// <exception cref="InvalidCurrentPasswordException">
    /// The account has no password set, or <paramref name="request"/>'s current password doesn't
    /// match the stored hash.
    /// </exception>
    Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
}
