using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
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

        // Configure gRPC client pointing to the API
        var apiAddress = builder.Configuration["ApiAddress"] ?? "https://localhost:7051";

        builder.Services.AddSingleton(sp =>
        {
            var channel = GrpcChannel.ForAddress(apiAddress);
            return new ActivityService.ActivityServiceClient(channel);
        });
        builder.Services.AddScoped<IActivityService, ActivityGrpcClient>();

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
