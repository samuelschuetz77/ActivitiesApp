using ActivitiesApp.Infrastructure.Data;
using ActivitiesApp.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;
using SharedActivity = ActivitiesApp.Shared.Models.Activity;

namespace ActivitiesApp.IntegrationTests;

public class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public PostgresDbContext CreateContext()
    {
        var opts = new DbContextOptionsBuilder<PostgresDbContext>()
            .UseNpgsql(_container.GetConnectionString(), o =>
                o.MigrationsAssembly("ActivitiesApp.Infrastructure.Migrations"))
            .Options;
        return new PostgresDbContext(opts);
    }

    public async Task<List<SharedActivity>> GetActivitiesAsync()
    {
        using var db = CreateContext();
        var rows = await db.Activities.AsNoTracking().Where(a => !a.IsDeleted).ToListAsync();
        return rows.Select(ToShared).ToList();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        using var db = CreateContext();
        await db.Database.MigrateAsync();
        await SeedAsync(db);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private static async Task SeedAsync(PostgresDbContext db)
    {
        db.Activities.AddRange(
            Make("Denver Pizza Palace",    "Restaurant",       cost: 12,  lat: 39.74, lng: -104.99, rating: 4.2),
            Make("Cheap Burger Spot",      "Fast Food",        cost: 8,   lat: 39.74, lng: -105.00, rating: 3.5),
            Make("Downtown Art Museum",    "Arts & Culture",   cost: 15,  lat: 39.74, lng: -104.98, rating: 4.8),
            Make("City Fitness Gym",       "Fitness & Sports", cost: 30,  lat: 39.74, lng: -104.99, rating: 4.0),
            Make("The Rusty Tap Room",     "Nightlife",        cost: 0,   lat: 39.73, lng: -104.99, rating: 3.8),
            Make("Riverfront Park",        "Outdoors",         cost: 0,   lat: 39.75, lng: -104.99, rating: 4.5),
            Make("Denver Mall",            "Shopping",         cost: 0,   lat: 39.73, lng: -104.98, rating: 3.9),
            Make("Kids Science Center",    "Attractions",      cost: 18,  lat: 39.74, lng: -104.99, rating: 4.7, minAge: 5, maxAge: 14),
            Make("Boulder Trail Network",  "Outdoors",         cost: 0,   lat: 40.01, lng: -105.27, rating: 4.9),
            Make("Boulder Brewing Tour",   "Nightlife",        cost: 20,  lat: 40.02, lng: -105.27, rating: 4.3),
            Make("Mountain Pizza Hut",     "Restaurant",       cost: 14,  lat: 40.01, lng: -105.28, rating: 3.6),
            Make("Garden of the Gods",     "Outdoors",         cost: 0,   lat: 38.83, lng: -104.88, rating: 4.9),
            Make("Fine Dining Steakhouse", "Restaurant",       cost: 75,  lat: 38.83, lng: -104.82, rating: 4.6)
        );
        await db.SaveChangesAsync();
    }

    private static Activity Make(string name, string category, double cost, double lat, double lng,
        double rating, int minAge = 0, int maxAge = 99)
        => new()
        {
            Name = name,
            City = "Denver",
            Description = "",
            Category = category,
            Cost = cost,
            Latitude = lat,
            Longitude = lng,
            MinAge = minAge,
            MaxAge = maxAge,
            Rating = rating,
        };

    public static SharedActivity ToShared(Activity a) => new()
    {
        Id = a.Id,
        Name = a.Name,
        City = a.City,
        Description = a.Description,
        Cost = a.Cost,
        Activitytime = a.Activitytime,
        Latitude = a.Latitude,
        Longitude = a.Longitude,
        MinAge = a.MinAge,
        MaxAge = a.MaxAge,
        Category = a.Category,
        ImageUrl = a.ImageUrl,
        PlaceId = a.PlaceId,
        Rating = a.Rating,
        UpdatedAt = a.UpdatedAt,
        IsDeleted = a.IsDeleted,
        CreatedByUserId = a.CreatedByUserId,
    };
}
