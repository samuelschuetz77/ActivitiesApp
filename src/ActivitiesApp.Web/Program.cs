using ActivitiesApp.Web.Components;
using ActivitiesApp.Shared.Services;
using ActivitiesApp.Web.Services;
using LocationService = ActivitiesApp.Shared.Services.LocationService;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

// new push to test pipeline

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Trust X-Forwarded-Proto from nginx ingress
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
#pragma warning disable ASPDEPR005
    options.KnownNetworks.Clear();
#pragma warning restore ASPDEPR005
    options.KnownProxies.Clear();
});

// Microsoft Entra ID authentication
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi(["api://6d3dc4ee-33ce-4656-95c8-702a38464687/access_as_user"])
    .AddInMemoryTokenCaches();
builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, (OpenIdConnectOptions options) =>
{
    options.Events ??= new OpenIdConnectEvents();

    options.Events.OnRemoteFailure = context =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("OpenIdConnect");
        logger.LogError(context.Failure,
            "OpenID Connect remote failure on {Path}",
            context.Request.Path);

        context.Response.Redirect("/Error");
        context.HandleResponse();
        return Task.CompletedTask;
    };

    options.Events.OnAuthenticationFailed = context =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("OpenIdConnect");
        logger.LogError(context.Exception, "OpenID Connect authentication failed on {Path}", context.Request.Path);

        context.Response.Redirect("/Error");
        context.HandleResponse();
        return Task.CompletedTask;
    };
});
builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add device-specific services used by the ActivitiesApp.Shared project
builder.Services.AddSingleton<IFormFactor, FormFactor>();

// Configure REST client pointing to the API (no gRPC — plain HTTP/1.1 JSON)
var apiAddress = builder.Environment.IsDevelopment()
    ? builder.Configuration["Services:activitiesapp-api:https:0"]
        ?? builder.Configuration["ApiAddress"]
        ?? "https://localhost:7051"
    : builder.Configuration["ApiAddress"]
        ?? "https://activities-api-g8adhabhb6eqbfd2.eastus-01.azurewebsites.net/";

builder.Services.AddHttpClient<ActivityRestClient>(client =>
{
    client.BaseAddress = new Uri(apiAddress);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<IActivityService>(sp => sp.GetRequiredService<ActivityRestClient>());
builder.Services.AddScoped<ILocationProvider, ActivitiesApp.Web.Services.JsLocationProvider>();
builder.Services.AddScoped<LocationService>();

var app = builder.Build();

var appVersion = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Web starting — Version={Version}, ApiAddress={ApiAddress} (REST), Env={Env}",
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

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();

// Diagnostic endpoints
app.MapGet("/api/version", () => Results.Ok(new
{
    version = appVersion,
    apiAddress,
    transport = "REST",
    environment = app.Environment.EnvironmentName,
    timestamp = DateTimeOffset.UtcNow
}));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(ActivitiesApp.Shared._Imports).Assembly);

app.Run();
