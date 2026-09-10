namespace Spot.Auth.Api.Models;

/// <summary>
/// Maps to the <c>refresh_tokens</c> table (db/Spot.sql). Only a SHA-256 hash of the token is
/// ever persisted — the raw value is returned to the client once, at issuance, and never stored.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = default!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = default!;
}
