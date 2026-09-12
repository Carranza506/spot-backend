using Microsoft.EntityFrameworkCore;
using Spot.Business.Api.Models;
using BusinessEntity = Spot.Business.Api.Models.Business;

namespace Spot.Business.Api.Data;

public class BusinessDbContext(DbContextOptions<BusinessDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<BusinessEntity> Businesses => Set<BusinessEntity>();
    public DbSet<BusinessOwner> BusinessOwners => Set<BusinessOwner>();
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

    /// <summary>Fixed point in time used for the seed rows below — HasData needs static values.</summary>
    private static readonly DateTimeOffset SeedTimestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<ContactType>();

        // Owned and migrated by Spot.Auth.Api; mapped here only so EF Core can express the
        // user_id foreign keys below. See Models/UserReference.cs.
        modelBuilder.Entity<UserReference>(e =>
        {
            e.ToTable("users", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
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
            e.HasIndex(x => x.Name).HasDatabaseName("idx_businesses_name");
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
            e.ToTable("business_photos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            e.Property(x => x.StorageKey).HasColumnName("storage_key").IsRequired();
            e.Property(x => x.Url).HasColumnName("url");
            e.Property(x => x.IsPrimary).HasColumnName("is_primary").HasDefaultValue(false);
            e.Property(x => x.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.BusinessId).HasDatabaseName("idx_business_photos_business");
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
            e.ToTable("service_photos");
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

        modelBuilder.Entity<BusinessOwner>(e =>
        {
            e.ToTable("business_owners");
            e.HasKey(x => new { x.BusinessId, x.UserId });
            e.Property(x => x.BusinessId).HasColumnName("business_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => x.UserId).HasDatabaseName("idx_business_owners_user");
            e.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
            // db/Spot.sql:56
            e.HasOne<UserReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
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
    }
}
