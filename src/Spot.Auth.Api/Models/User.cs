namespace Spot.Auth.Api.Models;

/// <summary>Maps to the <c>users</c> table (db/Spot.sql).</summary>
public class User
{
    public Guid Id { get; set; }

    /// <summary>Always stored lower-cased and trimmed — see UserService.NormalizeEmail.</summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// PHC-formatted PBKDF2 hash from <see cref="Microsoft.AspNetCore.Identity.PasswordHasher{TUser}"/>.
    /// Never the plain-text password. Null for accounts created via a social provider only.
    /// </summary>
    public string? PasswordHash { get; set; }

    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public string? Phone { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public UserRole Role { get; set; } = UserRole.CLIENT;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
