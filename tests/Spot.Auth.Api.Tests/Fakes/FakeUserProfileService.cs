using Spot.Auth.Api.DTOs;
using Spot.Auth.Api.Services;

namespace Spot.Auth.Api.Tests.Fakes;

/// <summary>In-memory stand-in for IUserProfileService — no database involved.</summary>
public sealed class FakeUserProfileService : IUserProfileService
{
    public UserDto? ProfileToReturn { get; set; }
    public Guid? LastRequestedUserId { get; private set; }
    public UpdateProfileRequest? LastUpdateRequest { get; private set; }

    public Task<UserDto?> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        LastRequestedUserId = userId;
        return Task.FromResult(ProfileToReturn);
    }

    public Task<UserDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        LastRequestedUserId = userId;
        LastUpdateRequest = request;
        return Task.FromResult(ProfileToReturn);
    }

    public void Reset()
    {
        ProfileToReturn = null;
        LastRequestedUserId = null;
        LastUpdateRequest = null;
    }
}
