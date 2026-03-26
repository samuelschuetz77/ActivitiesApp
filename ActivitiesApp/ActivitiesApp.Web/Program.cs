using ActivitiesApp.Web.Components;
using ActivitiesApp.Shared.Services;
using ActivitiesApp.Web.Services;
using LocationService = ActivitiesApp.Shared.Services.LocationService;
using ActivitiesApp.Protos;
using Grpc.Net.Client;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add device-specific services used by the ActivitiesApp.Shared project
builder.Services.AddSingleton<IFormFactor, FormFactor>();

// Configure gRPC client pointing to the API
var apiAddress = builder.Configuration["Services:activitiesapp-api:https:0"]
    ?? builder.Configuration["ApiAddress"]
    ?? "https://localhost:7051";

builder.Services.AddSingleton(sp =>
{
    var channel = GrpcChannel.ForAddress(apiAddress);
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

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Skip HTTPS redirect in production k8s (TLS terminates at ingress)
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

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
