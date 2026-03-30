using ActivitiesApp.Infrastructure.Data;
using ActivitiesApp.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace ActivitiesApp.Infrastructure.Services;

public class CosmosSeedService
{
    private readonly AppDbContext _cosmos;
    private readonly PostgresDbContext _postgres;
    private readonly ILogger<CosmosSeedService> _logger;

    public CosmosSeedService(AppDbContext cosmos, PostgresDbContext postgres, ILogger<CosmosSeedService> logger)
    {
        _cosmos = cosmos;
        _postgres = postgres;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Cosmos -> Postgres seed starting");

        try
        {
            // Ensure Cosmos container exists before reading
            await _cosmos.Database.EnsureCreatedAsync();

            var cosmosActivities = await _cosmos.Activities.ToListAsync();
            _logger.LogInformation("Read {Count} activities from Cosmos", cosmosActivities.Count);

            if (cosmosActivities.Count == 0)
            {
                _logger.LogInformation("No Cosmos data to seed — skipping");
                return;
            }

            var existingPg = await _postgres.Activities.ToListAsync();
            var pgById = existingPg.ToDictionary(a => a.Id);
            var pgByPlaceId = existingPg
                .Where(a => !string.IsNullOrEmpty(a.PlaceId))
                .GroupBy(a => a.PlaceId!)
                .ToDictionary(g => g.Key, g => g.First());

            int inserted = 0, updated = 0, skipped = 0;

            foreach (var source in cosmosActivities)
            {
                // Match by PlaceId first (preferred external identity), then by Id
                Activity? existing = null;

                if (!string.IsNullOrEmpty(source.PlaceId) && pgByPlaceId.TryGetValue(source.PlaceId, out var byPlace))
                    existing = byPlace;
                else if (pgById.TryGetValue(source.Id, out var byId))
                    existing = byId;

                if (existing != null)
                {
                    // Update if Cosmos version is newer
                    if (source.UpdatedAt > existing.UpdatedAt)
                    {
                        existing.Name = source.Name;
                        existing.City = source.City;
                        existing.Description = source.Description;
                        existing.Cost = source.Cost;
                        existing.Activitytime = source.Activitytime;
                        existing.Latitude = source.Latitude;
                        existing.Longitude = source.Longitude;
                        existing.MinAge = source.MinAge;
                        existing.MaxAge = source.MaxAge;
                        existing.Category = source.Category;
                        existing.ImageUrl = source.ImageUrl;
                        existing.PlaceId = source.PlaceId;
                        existing.Rating = source.Rating;
                        existing.IsDeleted = source.IsDeleted;
                        existing.UpdatedAt = source.UpdatedAt;
                        updated++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                else
                {
                    // Insert new row — use the same Id from Cosmos
                    _postgres.Activities.Add(new Activity
                    {
                        Id = source.Id,
                        Name = source.Name,
                        City = source.City,
                        Description = source.Description,
                        Cost = source.Cost,
                        Activitytime = source.Activitytime,
                        Latitude = source.Latitude,
                        Longitude = source.Longitude,
                        MinAge = source.MinAge,
                        MaxAge = source.MaxAge,
                        Category = source.Category,
                        ImageUrl = source.ImageUrl,
                        PlaceId = source.PlaceId,
                        Rating = source.Rating,
                        IsDeleted = source.IsDeleted,
                        UpdatedAt = source.UpdatedAt
                    });
                    inserted++;
                }
            }

            await _postgres.SaveChangesAsync();
            sw.Stop();

            _logger.LogInformation(
                "Cosmos -> Postgres seed complete in {Duration}ms — inserted={Inserted}, updated={Updated}, skipped={Skipped}",
                sw.ElapsedMilliseconds, inserted, updated, skipped);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Cosmos -> Postgres seed FAILED after {Duration}ms", sw.ElapsedMilliseconds);
            _logger.LogWarning("Seed failed — starting with existing Postgres data");
        }
    }
}
