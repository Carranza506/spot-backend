using Spot.Auth.Api.Models;

namespace Spot.Auth.Api.DTOs;

/// <summary>Matches the "User" schema in contracts/spot-api.yaml. Never carries PasswordHash.</summary>
public sealed record UserDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? Phone,
    string? ProfilePhotoUrl,
    string Role,
    bool IsActive,
    IReadOnlyList<string> LinkedProviders,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Requires <see cref="User.AuthProviders"/> to already be loaded (see IUserRepository.GetByIdAsync).</summary>
    public static UserDto FromEntity(User user) => new(
        Id: user.Id,
        Email: user.Email,
        FirstName: user.FirstName,
        LastName: user.LastName,
        Phone: user.Phone,
        ProfilePhotoUrl: user.ProfilePhotoUrl,
        Role: user.Role.ToString(),
        IsActive: user.IsActive,
        LinkedProviders: user.AuthProviders.Select(p => p.Provider.ToString()).ToList(),
        CreatedAt: user.CreatedAt,
        UpdatedAt: user.UpdatedAt);
}
