using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Repositories;

public interface IBookingRepository
{
    /// <summary>
    /// Inserts <paramref name="booking"/>; id, created_at and updated_at come from the database
    /// defaults and are populated on the entity afterwards. Overlaps are not checked up front: the
    /// excl_active_booking_overlap constraint decides, atomically, even under concurrent requests.
    /// </summary>
    /// <exception cref="Services.BookingSlotOverlapException">
    /// The insert violated excl_active_booking_overlap (SQLSTATE 23P01). Any other database error
    /// propagates unchanged.
    /// </exception>
    Task CreateAsync(BookingEntity booking, CancellationToken ct = default);
}
