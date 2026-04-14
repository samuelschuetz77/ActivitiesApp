using ActivitiesApp.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace ActivitiesApp.Infrastructure.Data
{
    public class AppDbContext : DbContext, IActivityDbContext
    {
        public DbSet<Activity> Activities { get; set; }
        public DbSet<GoogleApiDailyUsage> GoogleApiDailyUsages { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Activity>()
                .ToContainer("Activities")
                .HasPartitionKey(a => a.City)
                .UseETagConcurrency();

            modelBuilder.Entity<Activity>()
                .Property(a => a.Id)
                .ToJsonProperty("id");

            modelBuilder.Entity<Activity>()
                .Property(a => a.City)
                .ToJsonProperty("City");

            modelBuilder.Entity<Activity>()
                .Property(a => a.Name)
                .ToJsonProperty("name");

            modelBuilder.Entity<Activity>()
                .Property(a => a.Description)
                .ToJsonProperty("description");

            modelBuilder.Entity<Activity>()
                .Property(a => a.Cost)
                .ToJsonProperty("cost");

            modelBuilder.Entity<Activity>()
                .Property(a => a.Activitytime)
                .ToJsonProperty("activitytime");

            modelBuilder.Entity<Activity>()
                .Property(a => a.Latitude)
                .ToJsonProperty("latitude");

            modelBuilder.Entity<Activity>()
                .Property(a => a.Longitude)
                .ToJsonProperty("longitude");

            modelBuilder.Entity<Activity>()
                .Property(a => a.MinAge)
                .ToJsonProperty("minAge");

            modelBuilder.Entity<Activity>()
                .Property(a => a.MaxAge)
                .ToJsonProperty("maxAge");

            modelBuilder.Entity<Activity>()
                .Property(a => a.Category)
                .ToJsonProperty("category");

            modelBuilder.Entity<Activity>()
                .Property(a => a.ImageUrl)
                .ToJsonProperty("imageUrl");

            modelBuilder.Entity<Activity>()
                .Property(a => a.PlaceId)
                .ToJsonProperty("placeId");

            modelBuilder.Entity<Activity>()
                .Property(a => a.Rating)
                .ToJsonProperty("rating");

            modelBuilder.Entity<Activity>()
                .Property(a => a.UpdatedAt)
                .ToJsonProperty("updatedAt");

            modelBuilder.Entity<Activity>()
                .Property(a => a.IsDeleted)
                .ToJsonProperty("isDeleted");

            modelBuilder.Entity<Activity>()
                .Property(a => a.CreatedByUserId)
                .ToJsonProperty("createdByUserId");

            modelBuilder.Entity<GoogleApiDailyUsage>()
                .ToContainer("GoogleApiDailyUsage")
                .HasPartitionKey(u => u.ApiType)
                .UseETagConcurrency();

            modelBuilder.Entity<GoogleApiDailyUsage>()
                .Property(u => u.Id)
                .ToJsonProperty("id");

            modelBuilder.Entity<GoogleApiDailyUsage>()
                .Property(u => u.UsageDate)
                .ToJsonProperty("usageDate");

            modelBuilder.Entity<GoogleApiDailyUsage>()
                .Property(u => u.ApiType)
                .ToJsonProperty("apiType");

            modelBuilder.Entity<GoogleApiDailyUsage>()
                .Property(u => u.RequestCount)
                .ToJsonProperty("requestCount");

            modelBuilder.Entity<GoogleApiDailyUsage>()
                .Property(u => u.UpdatedAt)
                .ToJsonProperty("updatedAt");
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var entry in ChangeTracker.Entries<Activity>())
            {
                if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
                {
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
}
