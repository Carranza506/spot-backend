using Microsoft.EntityFrameworkCore;
using Spot.Booking.Api.Models;
using BookingEntity = Spot.Booking.Api.Models.Booking;

namespace Spot.Booking.Api.Data;

public class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<BookingEntity> Bookings => Set<BookingEntity>();
    public DbSet<BusinessReference> Businesses => Set<BusinessReference>();
    public DbSet<BusinessHoursReference> BusinessHours => Set<BusinessHoursReference>();
    public DbSet<BusinessScheduleExceptionReference> BusinessScheduleExceptions => Set<BusinessScheduleExceptionReference>();
    public DbSet<ServiceReference> Services => Set<ServiceReference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<BookingStatus>();

        // Owned and migrated by Spot.Auth.Api; mapped here only so EF Core can express the
        // user_id foreign key below. See Models/UserReference.cs.
        modelBuilder.Entity<UserReference>(e =>
        {
            e.ToTable("users", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
        });

        // Owned and migrated by Spot.Business.Api; mapped read-only (only the columns availability
        // and the bookings ownership checks need, #66/#68) and excluded from this service's
        // migrations. See Models/BusinessReference.cs.
        modelBuilder.Entity<BusinessReference>(e =>
        {
            e.ToTable("businesses", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<BusinessHoursReference>(e =>
        {
            e.ToTable("business_hours", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            e.Property(x => x.DayOfWeek).HasColumnName("day_of_week");
            e.Property(x => x.OpenTime).HasColumnName("open_time");
            e.Property(x => x.CloseTime).HasColumnName("close_time");
            e.Property(x => x.IsClosed).HasColumnName("is_closed");
        });

        modelBuilder.Entity<BusinessScheduleExceptionReference>(e =>
        {
            e.ToTable("business_schedule_exceptions", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            e.Property(x => x.ExceptionDate).HasColumnName("exception_date");
            e.Property(x => x.IsClosed).HasColumnName("is_closed");
            e.Property(x => x.OpenTime).HasColumnName("open_time");
            e.Property(x => x.CloseTime).HasColumnName("close_time");
        });

        modelBuilder.Entity<ServiceReference>(e =>
        {
            e.ToTable("services", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            // Name and price are copied onto a new booking (#67).
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
            e.Property(x => x.Price).HasColumnName("price").HasColumnType("numeric(12,2)");
            e.Property(x => x.DurationMinutes).HasColumnName("duration_minutes");
            e.Property(x => x.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<BookingEntity>(e =>
        {
            e.ToTable("bookings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            e.Property(x => x.ServiceId).HasColumnName("service_id");
            e.Property(x => x.StartAt).HasColumnName("start_at");
            e.Property(x => x.EndAt).HasColumnName("end_at");
            // Not HasDefaultValue(BookingStatus.PENDING): for a native Postgres enum column,
            // EF/Npgsql renders that as a bare "DEFAULT 0" (the enum's underlying int), which
            // Postgres rejects for an enum-typed column ("default expression is of type
            // integer") — the same bug found and fixed for Spot.Auth.Api's users.role.
            // HasDefaultValueSql with an explicit cast is required instead; lowercase to match
            // what HasPostgresEnum<BookingStatus>()'s naming convention actually creates the
            // booking_status type with (see also this migration's own note on the identical
            // lowercase-vs-uppercase-Spot.sql-labels discrepancy in the exclusion constraint).
            e.Property(x => x.Status).HasColumnName("status").HasColumnType("booking_status").HasDefaultValueSql("'pending'::booking_status");
            e.Property(x => x.ServiceName).HasColumnName("service_name").HasMaxLength(255).IsRequired();
            e.Property(x => x.ServicePrice).HasColumnName("service_price").HasColumnType("numeric(10,2)");
            e.Property(x => x.ServiceDurationMinutes).HasColumnName("service_duration_minutes");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => new { x.UserId, x.StartAt }).HasDatabaseName("idx_bookings_user_start");
            e.HasIndex(x => new { x.BusinessId, x.StartAt }).HasDatabaseName("idx_bookings_business_start");
            e.HasIndex(x => x.ServiceId).HasDatabaseName("idx_bookings_service");
            // db/Spot.sql:123 (plain REFERENCES, i.e. ON DELETE NO ACTION)
            e.HasOne<UserReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
