using Microsoft.EntityFrameworkCore;

namespace ActivitiesApp.Data;

public class LocalDbContext : DbContext
{
    public DbSet<LocalActivity> Activities { get; set; }
    public DbSet<SyncMetadata> SyncMetadata { get; set; }

    public LocalDbContext(DbContextOptions<LocalDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalActivity>()
            .HasKey(a => a.Id);

        modelBuilder.Entity<SyncMetadata>()
            .HasKey(s => s.Id);
    }
}
