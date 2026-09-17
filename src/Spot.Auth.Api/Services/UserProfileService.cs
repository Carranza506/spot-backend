using Spot.Auth.Api.DTOs;
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
