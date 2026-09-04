namespace Spot.Auth.Api.Models;

public class UserAuthProvider
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AuthProvider Provider { get; set; }
    public string ProviderUserId { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
