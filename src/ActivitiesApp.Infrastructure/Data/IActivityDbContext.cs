using ActivitiesApp.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace ActivitiesApp.Infrastructure.Data;

public interface IActivityDbContext
{
    DbSet<Activity> Activities { get; set; }
    DbSet<GoogleApiDailyUsage> GoogleApiDailyUsages { get; set; }
    DbSet<UserSettings> UserSettings { get; set; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
