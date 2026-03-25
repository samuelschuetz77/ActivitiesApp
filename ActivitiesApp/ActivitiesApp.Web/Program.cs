using ActivitiesApp.Web.Components;
using ActivitiesApp.Shared.Services;
using ActivitiesApp.Web.Services;
using LocationService = ActivitiesApp.Shared.Services.LocationService;
using ActivitiesApp.Protos;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add device-specific services used by the ActivitiesApp.Shared project
builder.Services.AddSingleton<IFormFactor, FormFactor>();

// The deployed API serves gRPC-Web over HTTP/1.1 on port 80. Native gRPC over cleartext
// does not work reliably there because Kestrel falls back to HTTP/1.1 without TLS.
var apiAddress = builder.Configuration["Services:activitiesapp-api:https:0"]
    ?? builder.Configuration["ApiAddress"]
    ?? "https://localhost:7051";

builder.Services.AddSingleton(apiAddress); // expose for diagnostics
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    logger.LogInformation("Connecting to API at {ApiAddress} via gRPC-Web/HTTP1.1", apiAddress);
    var innerHandler = new GrpcLoggingHandler(
        new HttpClientHandler(),
        loggerFactory.CreateLogger<GrpcLoggingHandler>());
    var grpcWebHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, innerHandler);
    var httpClient = new HttpClient(grpcWebHandler)
    {
        DefaultRequestVersion = new Version(1, 1),
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
    };
    var channel = GrpcChannel.ForAddress(apiAddress, new GrpcChannelOptions { HttpClient = httpClient });
    return new ActivityService.ActivityServiceClient(channel);
});
builder.Services.AddScoped<IActivityService, ActivityGrpcClient>();
builder.Services.AddSingleton<LocationService>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(ActivitiesApp.Shared._Imports).Assembly);

app.Run();
