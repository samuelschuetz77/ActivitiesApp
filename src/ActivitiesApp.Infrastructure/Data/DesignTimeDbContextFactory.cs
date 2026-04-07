using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActivitiesApp.Infrastructure.Data;

public sealed class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PostgresDbContext>
{
    public PostgresDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PostgresDbContext>();
        optionsBuilder.UseNpgsql(GetConnectionString(), npgsql =>
            npgsql.MigrationsAssembly("ActivitiesApp.Infrastructure.Migrations"));

        return new PostgresDbContext(optionsBuilder.Options);
    }

    private static string GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=activitiesdb;Username=activitiesapp;Password=dev";
    }
}

public sealed class AppIdentityDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppIdentityDbContext>
{
    public AppIdentityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppIdentityDbContext>();
        optionsBuilder.UseNpgsql(GetConnectionString(), npgsql =>
            npgsql.MigrationsAssembly("ActivitiesApp.Infrastructure.Migrations"));

        return new AppIdentityDbContext(optionsBuilder.Options);
    }

    private static string GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=activitiesdb;Username=activitiesapp;Password=dev";
    }
}
