namespace Spot.Booking.Api.Models;

/// <summary>
/// Minimal read-only reference to the `users` table, which lives in the same shared "spot_dev"
/// Postgres database (see README.md > Database) but is owned and migrated exclusively by
/// Spot.Auth.Api. Mapped here, with migrations excluded, so EF Core can model the bookings.user_id
/// foreign key and read the client's name for the Booking response (#152).
/// </summary>
public class UserReference
{
    public Guid Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
