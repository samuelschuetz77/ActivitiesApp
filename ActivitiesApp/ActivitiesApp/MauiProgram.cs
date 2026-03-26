using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using ActivitiesApp.Data;
using ActivitiesApp.Shared.Services;
using ActivitiesApp.Services;
using ActivitiesApp.Protos;
using Grpc.Net.Client;

namespace ActivitiesApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Configuration.AddUserSecrets<App>();

        // Add device-specific services used by the ActivitiesApp.Shared project
        builder.Services.AddSingleton<IFormFactor, FormFactor>();
        builder.Services.AddSingleton<IConnectivity>(Connectivity.Current);

        // Configure gRPC client pointing to the API
        var apiAddress = builder.Configuration["ApiAddress"] ?? "https://localhost:7051";

        builder.Services.AddSingleton(sp =>
        {
            var channel = GrpcChannel.ForAddress(apiAddress);
            return new ActivityService.ActivityServiceClient(channel);
        });
        builder.Services.AddSingleton(apiAddress);

        // Local SQLite database
        builder.Services.AddDbContext<LocalDbContext>(options =>
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "activities_cache.db");
            options.UseSqlite($"Data Source={dbPath}");
        });

        // Background location tracking (singleton — polls every 3 min)
        builder.Services.AddSingleton<ActivitiesApp.Shared.Services.LocationService>();

        // In-memory activity cache (singleton — instant reads for all pages)
        builder.Services.AddSingleton<ActivityCacheService>();

        // Sync service (singleton — manages timer and connectivity events)
        builder.Services.AddSingleton<SyncService>();

        // Offline-first activity service replaces direct gRPC client for MAUI
        builder.Services.AddScoped<IActivityService, OfflineActivityService>();

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
