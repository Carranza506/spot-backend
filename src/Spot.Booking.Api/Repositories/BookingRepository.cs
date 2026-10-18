using Microsoft.EntityFrameworkCore;
using Npgsql;
using Spot.Booking.Api.Data;
using Spot.Booking.Api.Services;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Repositories;

public sealed class BookingRepository(BookingDbContext db) : IBookingRepository
{
    /// <summary>The exclusion constraint created by the AddUserIdForeignKeyTriggerAndOverlapExclusion migration.</summary>
    public const string OverlapConstraint = "excl_active_booking_overlap";

    public async Task CreateAsync(BookingEntity booking, CancellationToken ct = default)
    {
        db.Bookings.Add(booking);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           {
                                               SqlState: PostgresErrorCodes.ExclusionViolation,
                                               ConstraintName: OverlapConstraint,
                                           })
        {
            throw new BookingSlotOverlapException(ex);
        }
    }
}
