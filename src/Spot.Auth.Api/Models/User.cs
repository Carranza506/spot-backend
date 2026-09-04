namespace Spot.Auth.Api.Models;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string? PasswordHash { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? Phone { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public UserRole Role { get; set; } = UserRole.CLIENT;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<UserAuthProvider> AuthProviders { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
