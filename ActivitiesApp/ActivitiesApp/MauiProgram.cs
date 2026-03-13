using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ActivitiesApp.Shared.Services;
using ActivitiesApp.Shared.Data;
using ActivitiesApp.Services;

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

        // Add Cosmos DB context — reads credentials from user secrets
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseCosmos(
                accountEndpoint: builder.Configuration["CosmosDb:Endpoint"]!,
                accountKey: builder.Configuration["CosmosDb:Key"]!,
                databaseName: "ActivitiesDb"
            ));

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
