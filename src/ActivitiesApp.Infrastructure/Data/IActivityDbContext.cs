using ActivitiesApp.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace ActivitiesApp.Infrastructure.Data;

public interface IActivityDbContext
{
    DbSet<Activity> Activities { get; set; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
