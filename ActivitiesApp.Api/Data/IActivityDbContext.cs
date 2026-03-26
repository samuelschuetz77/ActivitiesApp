using ActivitiesApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ActivitiesApp.Api.Data;

public interface IActivityDbContext
{
    DbSet<Activity> Activities { get; set; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
