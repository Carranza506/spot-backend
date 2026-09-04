using Microsoft.EntityFrameworkCore;
using Spot.Booking.Api.Models;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Data;

public class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<BookingEntity> Bookings => Set<BookingEntity>();
    public DbSet<BookingService> BookingServices => Set<BookingService>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<BookingStatus>();

        modelBuilder.Entity<BookingEntity>(e =>
        {
            e.ToTable("bookings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            e.Property(x => x.StartAt).HasColumnName("start_at");
            e.Property(x => x.EndAt).HasColumnName("end_at");
            e.Property(x => x.Status).HasColumnName("status").HasColumnType("booking_status").HasDefaultValue(BookingStatus.PENDING);
            e.Property(x => x.TotalPrice).HasColumnName("total_price").HasColumnType("numeric(12,2)").HasDefaultValue(0m);
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => new { x.UserId, x.StartAt }).HasDatabaseName("idx_bookings_user_start");
            e.HasIndex(x => new { x.BusinessId, x.StartAt }).HasDatabaseName("idx_bookings_business_start");
        });

        modelBuilder.Entity<BookingService>(e =>
        {
            e.ToTable("booking_services");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.BookingId).HasColumnName("booking_id");
            e.Property(x => x.ServiceId).HasColumnName("service_id");
            e.Property(x => x.ServiceName).HasColumnName("service_name").HasMaxLength(150).IsRequired();
            e.Property(x => x.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(12,2)");
            e.Property(x => x.DurationMinutes).HasColumnName("duration_minutes");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.BookingId).HasDatabaseName("idx_booking_services_booking");
            e.HasOne(x => x.Booking).WithMany(b => b.Services).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
