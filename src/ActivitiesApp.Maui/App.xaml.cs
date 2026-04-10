using ActivitiesApp.Data;
using ActivitiesApp.Pages;
using ActivitiesApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ActivitiesApp;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            // Ensure SQLite database is created
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            await db.Database.EnsureCreatedAsync();

            // Warm the in-memory cache from SQLite before any page renders
            var cache = _serviceProvider.GetRequiredService<ActivityCacheService>();
            await cache.LoadFromDbAsync();

            // Start background auto-sync
            var syncService = _serviceProvider.GetRequiredService<SyncService>();
            syncService.StartAutoSync();
        }
        catch (Exception ex)
        {
            var logger = _serviceProvider.GetService<ILogger<App>>();
            logger?.LogError(ex, "Failed to initialize offline sync");
            System.Diagnostics.Debug.WriteLine($"Sync init failed: {ex}");
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var authService = _serviceProvider.GetRequiredService<AuthService>();

        // Try to restore a cached session
        _ = Task.Run(async () =>
        {
            var restored = await authService.TryRestoreSessionAsync();
            if (!restored)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Windows[0].Page = _serviceProvider.GetRequiredService<LoginPage>();
                });
            }
        });

        // Start with AppShell optimistically; LoginPage will replace it if needed
        return new Window(new AppShell()) { Title = "ActivitiesApp" };
    }
}
