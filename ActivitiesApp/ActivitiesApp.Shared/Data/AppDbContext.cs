using ActivitiesApp.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace ActivitiesApp.Shared.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Activity> Activities { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Activity>()
                .ToContainer("Activities")
                .HasPartitionKey(a => a.City);

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
        }
    }
}
