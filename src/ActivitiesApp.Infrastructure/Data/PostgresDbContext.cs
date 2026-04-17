using ActivitiesApp.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace ActivitiesApp.Infrastructure.Data;

public class PostgresDbContext : DbContext, IActivityDbContext
{
    public DbSet<Activity> Activities { get; set; }
    public DbSet<GoogleApiDailyUsage> GoogleApiDailyUsages { get; set; }
    public DbSet<UserSettings> UserSettings { get; set; }

    public PostgresDbContext(DbContextOptions<PostgresDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Activity>(entity =>
        {
            entity.ToTable("activities");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).HasColumnName("id");
            entity.Property(a => a.City).HasColumnName("city").HasMaxLength(200);
            entity.Property(a => a.Name).HasColumnName("name").HasMaxLength(500);
            entity.Property(a => a.Description).HasColumnName("description");
            entity.Property(a => a.Cost).HasColumnName("cost");
            entity.Property(a => a.Activitytime)
                .HasColumnName("activitytime")
                .HasColumnType("timestamp without time zone");
            entity.Property(a => a.Latitude).HasColumnName("latitude");
            entity.Property(a => a.Longitude).HasColumnName("longitude");
            entity.Property(a => a.MinAge).HasColumnName("min_age");
            entity.Property(a => a.MaxAge).HasColumnName("max_age");
            entity.Property(a => a.Category).HasColumnName("category").HasMaxLength(200);
            entity.Property(a => a.ImageUrl).HasColumnName("image_url");
            entity.Property(a => a.PlaceId).HasColumnName("place_id").HasMaxLength(300);
            entity.Property(a => a.Rating).HasColumnName("rating");
            entity.Property(a => a.UpdatedAt).HasColumnName("updated_at");
            entity.Property(a => a.IsDeleted).HasColumnName("is_deleted");
            entity.Property(a => a.CreatedByUserId).HasColumnName("created_by_user_id").HasMaxLength(450);
            entity.Property(a => a.CreatedByDisplayName).HasColumnName("created_by_display_name").HasMaxLength(500);
            entity.Property(a => a.CreatedByProfilePictureUrl).HasColumnName("created_by_profile_picture_url");

            entity.HasIndex(a => a.PlaceId);
            entity.HasIndex(a => a.City);
            entity.HasIndex(a => a.UpdatedAt);
            entity.HasIndex(a => a.CreatedByUserId);
        });

        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.ToTable("user_settings");
            entity.HasKey(u => u.UserId);
            entity.Property(u => u.UserId).HasColumnName("user_id").HasMaxLength(450);
            entity.Property(u => u.ProfilePictureUrl).HasColumnName("profile_picture_url");
        });

        modelBuilder.Entity<GoogleApiDailyUsage>(entity =>
        {
            entity.ToTable("google_api_daily_usage");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).HasColumnName("id").HasMaxLength(64);
            entity.Property(u => u.UsageDate).HasColumnName("usage_date");
            entity.Property(u => u.ApiType).HasColumnName("api_type").HasMaxLength(64);
            entity.Property(u => u.RequestCount).HasColumnName("request_count");
            entity.Property(u => u.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(u => u.UsageDate);
            entity.HasIndex(u => new { u.UsageDate, u.ApiType }).IsUnique();
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Activity>())
        {
            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                // Activitytime is an event/local wall-clock time, not an absolute instant.
                // Normalize the kind so Npgsql writes it to "timestamp without time zone".
                entry.Entity.Activitytime = DateTime.SpecifyKind(entry.Entity.Activitytime, DateTimeKind.Unspecified);
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<GoogleApiDailyUsage>())
        {
            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
