using Microsoft.EntityFrameworkCore;
using Spot.Business.Api.Models;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Data;

public class BusinessDbContext(DbContextOptions<BusinessDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<BusinessEntity> Businesses => Set<BusinessEntity>();
    public DbSet<BusinessCategory> BusinessCategories => Set<BusinessCategory>();
    public DbSet<BusinessLocation> BusinessLocations => Set<BusinessLocation>();
    public DbSet<BusinessContact> BusinessContacts => Set<BusinessContact>();
    public DbSet<BusinessPhoto> BusinessPhotos => Set<BusinessPhoto>();
    public DbSet<BusinessHours> BusinessHours => Set<BusinessHours>();
    public DbSet<BusinessScheduleException> BusinessScheduleExceptions => Set<BusinessScheduleException>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<ServicePhoto> ServicePhotos => Set<ServicePhoto>();
    public DbSet<FavoriteBusiness> FavoriteBusinesses => Set<FavoriteBusiness>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<BookingReference> Bookings => Set<BookingReference>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();

    /// <summary>Fixed point in time used for the seed rows below — HasData needs static values.</summary>
    private static readonly DateTimeOffset SeedTimestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Seeded plan ids (#138), fixed so HasData is stable across migrations.</summary>
    public static readonly Guid FreePlanId = Guid.Parse("0f3c2a6e-5b1d-4c8e-9a7f-1e2d3c4b5a60");
    public static readonly Guid ProPlanId = Guid.Parse("7d9e1f2a-3b4c-4d5e-8f60-718293a4b5c6");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<ContactType>();

        // Billing (#138). Explicit UPPERCASE labels, same as Spot.AiSearch.Api's enums (see the note in
        // its AiSearchDbContext): they match db/Spot.sql and the contract exactly. Program.cs maps
        // them with a translator that keeps the C# names as they are.
        modelBuilder.HasPostgresEnum("subscription_status", ["ACTIVE", "PAST_DUE", "CANCELED", "EXPIRED"]);
        modelBuilder.HasPostgresEnum("subscription_payment_status", ["PAID", "FAILED"]);

        // Owned and migrated by Spot.Auth.Api; mapped here only so EF Core can express the
        // user_id foreign keys below. See Models/UserReference.cs.
        modelBuilder.Entity<UserReference>(e =>
        {
            e.ToTable("users", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
        });

        // Owned and migrated by Spot.Booking.Api; read-only here, for reviews (#70). No relationship
        // to reviews is modeled, so this adds nothing to Business.Api's migrations. See Models/BookingReference.cs.
        modelBuilder.Entity<BookingReference>(e =>
        {
            e.ToTable("bookings", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.BusinessId).HasColumnName("business_id");
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.ToTable("categories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.ParentCategoryId).HasColumnName("parent_category_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => new { x.ParentCategoryId, x.Name }).IsUnique();
            e.HasIndex(x => x.ParentCategoryId).HasDatabaseName("idx_categories_parent");
            e.HasOne(x => x.ParentCategory).WithMany(c => c.SubCategories).HasForeignKey(x => x.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);

            // db/Spot.sql:208-213. HasData needs stable, compile-time keys and values, so
            // (unlike the raw INSERT, which lets id/created_at/updated_at fall back to their
            // column defaults) these five rows pin fixed ids and a fixed timestamp instead of
            // gen_random_uuid()/CURRENT_TIMESTAMP.
            e.HasData(
                new
                {
                    Id = Guid.Parse("6228d226-c50a-43af-bbae-835a224f0335"),
                    Name = "Belleza",
                    Description = "Servicios relacionados con belleza y cuidado personal",
                    IsActive = true,
                    CreatedAt = SeedTimestamp,
                    UpdatedAt = SeedTimestamp,
                },
                new
                {
                    Id = Guid.Parse("fdc7b7cb-2529-4879-bf1c-ac82bb75d893"),
                    Name = "Salud",
                    Description = "Servicios relacionados con salud y bienestar",
                    IsActive = true,
                    CreatedAt = SeedTimestamp,
                    UpdatedAt = SeedTimestamp,
                },
                new
                {
                    Id = Guid.Parse("ae084e20-06ce-407f-9bbd-be08089a407c"),
                    Name = "Deportes",
                    Description = "Servicios e instalaciones deportivas",
                    IsActive = true,
                    CreatedAt = SeedTimestamp,
                    UpdatedAt = SeedTimestamp,
                },
                new
                {
                    Id = Guid.Parse("4a8d8583-9c61-44f5-93a6-19901475a98b"),
                    Name = "Restaurantes",
                    Description = "Restaurantes y establecimientos de comida",
                    IsActive = true,
                    CreatedAt = SeedTimestamp,
                    UpdatedAt = SeedTimestamp,
                },
                new
                {
                    Id = Guid.Parse("045bfe83-fd2c-4694-906b-8c2e968ae188"),
                    Name = "Automotriz",
                    Description = "Servicios relacionados con vehículos",
                    IsActive = true,
                    CreatedAt = SeedTimestamp,
                    UpdatedAt = SeedTimestamp,
                });
        });

        modelBuilder.Entity<BusinessEntity>(e =>
        {
            e.ToTable("businesses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
            e.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(180).IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.LegalName).HasColumnName("legal_name").HasMaxLength(200);
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(255);
            e.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(30);
            e.Property(x => x.Website).HasColumnName("website");
            e.Property(x => x.LogoUrl).HasColumnName("logo_url");
            e.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.AccountId).IsUnique();
            e.HasIndex(x => x.Name).HasDatabaseName("idx_businesses_name");
            // Owned by Spot.Auth.Api; see Models/UserReference.cs.
            e.HasOne<UserReference>().WithOne().HasForeignKey<BusinessEntity>(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BusinessLocation>(e =>
        {
            e.ToTable("business_locations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            e.Property(x => x.Address).HasColumnName("address").IsRequired();
            e.Property(x => x.City).HasColumnName("city").HasMaxLength(100);
            e.Property(x => x.Province).HasColumnName("province").HasMaxLength(100);
            e.Property(x => x.Country).HasColumnName("country").HasMaxLength(100).HasDefaultValue("Costa Rica");
            e.Property(x => x.PostalCode).HasColumnName("postal_code").HasMaxLength(20);
            e.Property(x => x.Location).HasColumnName("location").HasColumnType("geography(Point,4326)").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.BusinessId).IsUnique();
            e.HasIndex(x => x.Location).HasDatabaseName("idx_business_locations_geo").HasMethod("GIST");
            e.HasOne(x => x.Business).WithOne(b => b.Location).HasForeignKey<BusinessLocation>(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BusinessContact>(e =>
        {
            e.ToTable("business_contacts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            e.Property(x => x.Type).HasColumnName("type").HasColumnType("contact_type");
            e.Property(x => x.Value).HasColumnName("value").IsRequired();
            e.Property(x => x.IsPrimary).HasColumnName("is_primary").HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.BusinessId).HasDatabaseName("idx_business_contacts_business");
            e.HasOne(x => x.Business).WithMany(b => b.Contacts).HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BusinessPhoto>(e =>
        {
            // db/Spot.sql's unnamed CHECK, under the name Postgres generates for it, so both
            // schemas match (the earlier migrations never created it).
            e.ToTable("business_photos", t => t.HasCheckConstraint("business_photos_display_order_check", "display_order >= 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            e.Property(x => x.StorageKey).HasColumnName("storage_key").IsRequired();
            e.Property(x => x.Url).HasColumnName("url");
            e.Property(x => x.IsPrimary).HasColumnName("is_primary").HasDefaultValue(false);
            e.Property(x => x.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.BusinessId).HasDatabaseName("idx_business_photos_business");
            // At most one primary photo per business (#65). The app unsets the previous primary in
            // the same transaction; this partial index makes two concurrent uploads unable to
            // leave two primaries behind.
            e.HasIndex(x => x.BusinessId, "ux_business_photos_one_primary").IsUnique().HasFilter("is_primary");
            e.HasOne(x => x.Business).WithMany(b => b.Photos).HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BusinessHours>(e =>
        {
            e.ToTable("business_hours");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            e.Property(x => x.DayOfWeek).HasColumnName("day_of_week");
            e.Property(x => x.OpenTime).HasColumnName("open_time");
            e.Property(x => x.CloseTime).HasColumnName("close_time");
            e.Property(x => x.IsClosed).HasColumnName("is_closed").HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => new { x.BusinessId, x.DayOfWeek }).IsUnique();
            e.HasOne(x => x.Business).WithMany(b => b.Hours).HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BusinessScheduleException>(e =>
        {
            e.ToTable("business_schedule_exceptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            e.Property(x => x.ExceptionDate).HasColumnName("exception_date");
            e.Property(x => x.IsClosed).HasColumnName("is_closed").HasDefaultValue(false);
            e.Property(x => x.OpenTime).HasColumnName("open_time");
            e.Property(x => x.CloseTime).HasColumnName("close_time");
            e.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(255);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => new { x.BusinessId, x.ExceptionDate }).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.ExceptionDate }).HasDatabaseName("idx_schedule_exceptions_business_date");
            e.HasOne(x => x.Business).WithMany(b => b.ScheduleExceptions).HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Service>(e =>
        {
            e.ToTable("services");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Price).HasColumnName("price").HasColumnType("numeric(12,2)");
            e.Property(x => x.DurationMinutes).HasColumnName("duration_minutes");
            e.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.BusinessId).HasDatabaseName("idx_services_business");
            e.HasOne(x => x.Business).WithMany(b => b.Services).HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ServicePhoto>(e =>
        {
            // Same as business_photos: db/Spot.sql's CHECK, under Postgres' generated name.
            e.ToTable("service_photos", t => t.HasCheckConstraint("service_photos_display_order_check", "display_order >= 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.ServiceId).HasColumnName("service_id");
            e.Property(x => x.StorageKey).HasColumnName("storage_key").IsRequired();
            e.Property(x => x.Url).HasColumnName("url");
            e.Property(x => x.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.ServiceId).HasDatabaseName("idx_service_photos_service");
            e.HasOne(x => x.Service).WithMany(s => s.Photos).HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BusinessCategory>(e =>
        {
            e.ToTable("business_categories");
            e.HasKey(x => new { x.BusinessId, x.CategoryId });
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            e.Property(x => x.CategoryId).HasColumnName("category_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.CategoryId).HasDatabaseName("idx_business_categories_category");
            e.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FavoriteBusiness>(e =>
        {
            e.ToTable("favorite_businesses");
            e.HasKey(x => new { x.UserId, x.BusinessId });
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
            // db/Spot.sql:148
            e.HasOne<UserReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Review>(e =>
        {
            e.ToTable("reviews");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.BookingId).HasColumnName("booking_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            e.Property(x => x.Rating).HasColumnName("rating");
            e.Property(x => x.Comment).HasColumnName("comment");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.BookingId).IsUnique();
            e.HasIndex(x => x.BusinessId).HasDatabaseName("idx_reviews_business");
            e.HasOne(x => x.Business).WithMany(b => b.Reviews).HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            // db/Spot.sql:155
            e.HasOne<UserReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- Billing (#138) ----------

        modelBuilder.Entity<Plan>(e =>
        {
            e.ToTable("plans", t =>
            {
                t.HasCheckConstraint("ck_plans_code", "code IN ('FREE','PRO')");
                t.HasCheckConstraint("ck_plans_interval", "interval IN ('MONTHLY')");
                t.HasCheckConstraint("ck_plans_price_crc", "price_crc >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(20).HasConversion<string>();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.PriceCrc).HasColumnName("price_crc").HasColumnType("numeric(12,2)");
            e.Property(x => x.Interval).HasColumnName("interval").HasMaxLength(20).HasConversion<string>();
            e.Property(x => x.StripePriceId).HasColumnName("stripe_price_id").HasMaxLength(255);
            e.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            e.HasIndex(x => x.Code).IsUnique();

            // PRO's stripe_price_id is left null on purpose: the real Price id differs per Stripe
            // account, so it comes from Stripe:ProPriceId (see BillingService) instead of the seed.
            e.HasData(
                new Plan
                {
                    Id = FreePlanId,
                    Code = PlanCode.FREE,
                    Name = "Gratis",
                    PriceCrc = 0m,
                    Interval = BillingInterval.MONTHLY,
                    IsActive = true,
                },
                new Plan
                {
                    Id = ProPlanId,
                    Code = PlanCode.PRO,
                    Name = "Pro",
                    PriceCrc = 10000m,
                    Interval = BillingInterval.MONTHLY,
                    IsActive = true,
                });
        });

        modelBuilder.Entity<Subscription>(e =>
        {
            e.ToTable("subscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.PlanId).HasColumnName("plan_id");
            e.Property(x => x.Status).HasColumnName("status").HasColumnType("subscription_status");
            e.Property(x => x.StripeCustomerId).HasColumnName("stripe_customer_id").HasMaxLength(255).IsRequired();
            e.Property(x => x.StripeSubscriptionId).HasColumnName("stripe_subscription_id").HasMaxLength(255).IsRequired();
            e.Property(x => x.CurrentPeriodEnd).HasColumnName("current_period_end");
            e.Property(x => x.CancelAtPeriodEnd).HasColumnName("cancel_at_period_end").HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.StripeSubscriptionId).IsUnique();
            e.HasIndex(x => x.AccountId).HasDatabaseName("idx_subscriptions_account");
            // At most one ACTIVE/PAST_DUE subscription per account. A second paid checkout that
            // completes anyway (two tabs) is rejected here and only logged; see BillingService.
            e.HasIndex(x => x.AccountId, "ux_subscriptions_one_current").IsUnique().HasFilter("status IN ('ACTIVE','PAST_DUE')");
            // Owned by Spot.Auth.Api; see Models/UserReference.cs. Cascade, same as businesses.account_id.
            e.HasOne<UserReference>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SubscriptionPayment>(e =>
        {
            e.ToTable("subscription_payments", t => t.HasCheckConstraint("ck_subscription_payments_amount_crc", "amount_crc >= 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.SubscriptionId).HasColumnName("subscription_id");
            e.Property(x => x.AmountCrc).HasColumnName("amount_crc").HasColumnType("numeric(12,2)");
            e.Property(x => x.Status).HasColumnName("status").HasColumnType("subscription_payment_status");
            e.Property(x => x.StripeInvoiceId).HasColumnName("stripe_invoice_id").HasMaxLength(255).IsRequired();
            e.Property(x => x.PaidAt).HasColumnName("paid_at");
            e.HasIndex(x => x.StripeInvoiceId).IsUnique();
            e.HasIndex(x => x.SubscriptionId).HasDatabaseName("idx_subscription_payments_subscription");
            e.HasOne(x => x.Subscription).WithMany(s => s.Payments).HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
