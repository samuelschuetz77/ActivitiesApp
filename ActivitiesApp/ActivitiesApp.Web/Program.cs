using ActivitiesApp.Web.Components;
using ActivitiesApp.Shared.Services;
using ActivitiesApp.Web.Services;
using LocationService = ActivitiesApp.Shared.Services.LocationService;
using ActivitiesApp.Protos;
using Grpc.Net.Client;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net.Http;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Trust X-Forwarded-Proto from nginx ingress so Blazor component location hashes
// are computed with the correct HTTPS scheme (pod runs HTTP but ingress terminates TLS).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add device-specific services used by the ActivitiesApp.Shared project
builder.Services.AddSingleton<IFormFactor, FormFactor>();

// Configure gRPC client pointing to the API
var apiAddress = builder.Configuration["Services:activitiesapp-api:https:0"]
    ?? builder.Configuration["ApiAddress"]
    ?? "https://localhost:7051";

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
builder.Services.AddSingleton(sp =>
{
    var channel = GrpcChannel.ForAddress(apiAddress, new GrpcChannelOptions
    {
        HttpHandler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        }
    });
    return new ActivityService.ActivityServiceClient(channel);
});
builder.Services.AddScoped<IActivityService>(sp =>
    new ActivityGrpcClient(sp.GetRequiredService<ActivityService.ActivityServiceClient>(), apiAddress));
builder.Services.AddSingleton<LocationService>();

var app = builder.Build();

var appVersion = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Web starting — Version={Version}, ApiAddress={ApiAddress}, Env={Env}",
    appVersion, apiAddress, app.Environment.EnvironmentName);

app.UseForwardedHeaders();
app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePages();

// Skip HTTPS redirect in production k8s (TLS terminates at ingress)
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapStaticAssets();

// Diagnostic endpoints
app.MapGet("/api/version", () => Results.Ok(new
{
    version = appVersion,
    apiAddress,
    environment = app.Environment.EnvironmentName,
    timestamp = DateTimeOffset.UtcNow
}));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(ActivitiesApp.Shared._Imports).Assembly);

app.Run();
