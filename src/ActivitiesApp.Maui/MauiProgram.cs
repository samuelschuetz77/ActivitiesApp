using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using ActivitiesApp.Data;
using ActivitiesApp.Shared.Services;
using ActivitiesApp.Services;
using ActivitiesApp.Protos;
using ActivitiesApp.ViewModels;
using ActivitiesApp.Pages;
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
        builder.Services.AddSingleton<INetworkStatus, MauiNetworkStatus>();

        // MSAL auth service
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<IAccessTokenProvider>(sp => sp.GetRequiredService<AuthService>());
        builder.Services.AddSingleton<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider, MauiAuthenticationStateProvider>();
        builder.Services.AddAuthorizationCore();

        var apiAddress = builder.Configuration["ApiAddress"] ?? "https://activities-api-g8adhabhb6eqbfd2.eastus-01.azurewebsites.net";

        // REST HttpClient for OfflineActivityService (with auth header)
        builder.Services.AddSingleton(sp =>
        {
            var tokenProvider = sp.GetRequiredService<IAccessTokenProvider>();
            var handler = new AuthHeaderHandler(tokenProvider);
            var client = new HttpClient(handler) { BaseAddress = new Uri(apiAddress) };
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        });

        // gRPC client (still used by SyncService for delta sync)
        builder.Services.AddSingleton(sp =>
        {
            var channel = GrpcChannel.ForAddress(apiAddress, new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler()
            });
            return new ActivityService.ActivityServiceClient(channel);
        });
        builder.Services.AddSingleton<IActivitySyncClient, GrpcActivitySyncClient>();

        // Local SQLite database
        builder.Services.AddDbContext<LocalDbContext>(options =>
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "activities_cache.db");
            options.UseSqlite($"Data Source={dbPath}");
        });
        builder.Services.AddSingleton<ILocalActivityStore, EfLocalActivityStore>();

        // Native location provider (triggers Android permission prompt)
        builder.Services.AddSingleton<ActivitiesApp.Shared.Services.ILocationProvider, ActivitiesApp.Services.MauiLocationProvider>();

        // Background location tracking (singleton — polls every 3 min)
        builder.Services.AddSingleton<ActivitiesApp.Shared.Services.LocationService>();

        // In-memory activity cache (singleton — instant reads for all pages)
        builder.Services.AddSingleton<ActivityCacheService>();

        // Sync service (singleton — manages timer and connectivity events)
        builder.Services.AddSingleton<SyncService>();

        // Offline-first activity service replaces direct gRPC client for MAUI
        builder.Services.AddScoped<IActivityService, OfflineActivityService>();

        // ViewModels
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<ActivitiesViewModel>();
        builder.Services.AddTransient<CreateViewModel>();
        builder.Services.AddTransient<ProfileViewModel>(sp =>
            new ProfileViewModel(sp.GetRequiredService<AuthService>(), sp.GetRequiredService<HttpClient>()));

        // Pages
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<ActivitiesPage>();
        builder.Services.AddTransient<CreatePage>();
        builder.Services.AddTransient<ProfilePage>();
        builder.Services.AddTransient<LoginPage>();

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
