using Spot.Auth.Api.DTOs;
using Spot.Auth.Api.Services;

namespace Spot.Auth.Api.Tests.Fakes;

/// <summary>In-memory stand-in for IUserProfileService — no database involved.</summary>
public sealed class FakeUserProfileService : IUserProfileService
{
    public UserDto? ProfileToReturn { get; set; }
    public Guid? LastRequestedUserId { get; private set; }
    public UpdateProfileRequest? LastUpdateRequest { get; private set; }

    /// <summary>Thrown by <see cref="UpdateProfileAsync"/>, if set — e.g. a ProfileFieldNotAllowedException, to exercise AuthController's mapping of it to a 400 ApiError.</summary>
    public Exception? ExceptionToThrow { get; set; }

    public Task<UserDto?> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        LastRequestedUserId = userId;
        return Task.FromResult(ProfileToReturn);
    }

    public Task<UserDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        LastRequestedUserId = userId;
        LastUpdateRequest = request;

        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        return Task.FromResult(ProfileToReturn);
    }

    public void Reset()
    {
        ProfileToReturn = null;
        LastRequestedUserId = null;
        LastUpdateRequest = null;
        ExceptionToThrow = null;
    }
}
