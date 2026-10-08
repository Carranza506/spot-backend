using Spot.Auth.Api.DTOs;
using Spot.Auth.Api.Models;
using Spot.Auth.Api.Repositories;

namespace Spot.Auth.Api.Services;

public sealed class UserProfileService(IUserRepository userRepository) : IUserProfileService
{
    public async Task<UserDto?> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, ct);
        return user is null ? null : UserDto.FromEntity(user);
    }

    public async Task<UserDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, ct);
        if (user is null)
            return null;

        // The caller's role comes from the loaded user, never from the request: a BUSINESS
        // account's name is `businesses.name`, edited only via PATCH /business/businesses/me.
        // This must be what rejects the request — the ck_users_client_names DB CHECK only
        // guards CLIENT rows and would never fire for a BUSINESS row anyway (its names stay null).
        if (user.Role == UserRole.BUSINESS)
        {
            if (request.FirstName.IsSet)
                throw new ProfileFieldNotAllowedException(
                    nameof(UpdateProfileRequest.FirstName), "business name is edited through PATCH /business/businesses/me");

            if (request.LastName.IsSet)
                throw new ProfileFieldNotAllowedException(
                    nameof(UpdateProfileRequest.LastName), "business name is edited through PATCH /business/businesses/me");
        }

        if (request.FirstName.IsSet)
            user.FirstName = request.FirstName.Value!.Trim();

        if (request.LastName.IsSet)
            user.LastName = request.LastName.Value!.Trim();

        if (request.Phone.IsSet)
            user.Phone = string.IsNullOrWhiteSpace(request.Phone.Value) ? null : request.Phone.Value.Trim();

        if (request.ProfilePhotoUrl.IsSet)
            user.ProfilePhotoUrl = request.ProfilePhotoUrl.Value;

        await userRepository.SaveChangesAsync(user, ct);

        return UserDto.FromEntity(user);
    }
}
