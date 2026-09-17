using Spot.Auth.Api.DTOs;

namespace Spot.Auth.Api.Services;

public interface IAuthService
{
    /// <exception cref="Repositories.DuplicateEmailException">The email is already registered.</exception>
    Task<AuthResponseDto> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
}
