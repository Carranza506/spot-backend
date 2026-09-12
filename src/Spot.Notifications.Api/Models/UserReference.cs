namespace Spot.Notifications.Api.Models;

/// <summary>
/// Minimal read-only reference to the `users` table, which lives in the same shared "spot_dev"
/// Postgres database (see README.md > Database) but is owned and migrated exclusively by
/// Spot.Auth.Api. Mapped here, with migrations excluded, only so EF Core can model the
/// audit_logs.user_id foreign key.
/// </summary>
public class UserReference
{
    public Guid Id { get; set; }
}
