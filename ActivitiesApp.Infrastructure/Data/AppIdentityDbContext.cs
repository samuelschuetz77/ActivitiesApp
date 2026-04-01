using ActivitiesApp.Infrastructure.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ActivitiesApp.Infrastructure.Data;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Place Identity tables in their own schema to keep them separate from activity tables
        modelBuilder.HasDefaultSchema("identity");
    }
}
