using ActivitiesApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "postgres";
var db = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "activitiesdb";
var user = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "activitiesapp";
var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD")
    ?? throw new InvalidOperationException("POSTGRES_PASSWORD environment variable is required");

var connectionString = $"Host={host};Port=5432;Database={db};Username={user};Password={password}";

Console.WriteLine($"[migrate] Connecting: host={host} db={db} user={user}");

var options = new DbContextOptionsBuilder<PostgresDbContext>()
    .UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsAssembly("ActivitiesApp.Infrastructure.Migrations"))
    .Options;

using var context = new PostgresDbContext(options);
await context.Database.MigrateAsync();

Console.WriteLine("[migrate] Migrations applied successfully.");
