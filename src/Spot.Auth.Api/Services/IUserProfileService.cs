using Spot.Auth.Api.DTOs;

namespace Spot.Auth.Api.Services;

/// <summary>Business logic behind GET/PATCH /auth/me — the layer AuthController talks to instead of the repository directly.</summary>
public interface IUserProfileService
{
    /// <summary>Null if no user with that id exists.</summary>
    Task<UserDto?> GetProfileAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Applies only the fields present in <paramref name="request"/> (see <see cref="Optional{T}"/>)
    /// and returns the updated profile; null if no user with that id exists.
    /// </summary>
    Task<UserDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);
}
