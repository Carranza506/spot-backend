using Spot.Auth.Api.Models;

namespace Spot.Auth.Api.DTOs;

/// <summary>Matches the User schema in contracts/spot-api.yaml. Never carries PasswordHash.</summary>
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
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static UserDto FromEntity(User user) => new(
        Id: user.Id,
        Email: user.Email,
        FirstName: user.FirstName,
        LastName: user.LastName,
        Phone: user.Phone,
        ProfilePhotoUrl: user.ProfilePhotoUrl,
        Role: user.Role.ToString(),
        IsActive: user.IsActive,
        // Social login (Google, etc.) is a separate issue — a user created via /auth/register
        // never has a linked provider.
        LinkedProviders: [],
        CreatedAt: user.CreatedAt,
        UpdatedAt: user.UpdatedAt);
}
