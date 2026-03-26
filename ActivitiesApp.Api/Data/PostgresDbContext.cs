using ActivitiesApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ActivitiesApp.Api.Data;

public class PostgresDbContext : DbContext, IActivityDbContext
{
    public DbSet<Activity> Activities { get; set; }

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

            entity.HasIndex(a => a.PlaceId);
            entity.HasIndex(a => a.City);
            entity.HasIndex(a => a.UpdatedAt);
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

        return await base.SaveChangesAsync(cancellationToken);
    }
}
