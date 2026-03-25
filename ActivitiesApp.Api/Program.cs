using ActivitiesApp.Api.Data;
using ActivitiesApp.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(80, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
    });
});

builder.Services.AddGrpc();

// Choose database provider based on environment variable
var dbProvider = builder.Configuration["DATABASE_PROVIDER"] ?? "Cosmos";

if (dbProvider == "Postgres")
{
    var connectionString = builder.Configuration.GetConnectionString("ActivitiesDb")
        ?? throw new InvalidOperationException("ConnectionStrings:ActivitiesDb is required when DATABASE_PROVIDER=Postgres");
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(connectionString));
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseCosmos(
            accountEndpoint: builder.Configuration["CosmosDb:Endpoint"] ?? "https://localhost:8081/",
            accountKey: builder.Configuration["CosmosDb:Key"] ?? "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
            databaseName: "ActivitiesDb"
        ));
}

// Register Google Places service with HttpClient
builder.Services.AddHttpClient<GooglePlacesService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await context.Database.EnsureCreatedAsync();
}

app.MapDefaultEndpoints();

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var contentType = context.Request.ContentType ?? "(none)";

    logger.LogInformation("HTTP request starting: {Method} {Path} content-type={ContentType}",
        context.Request.Method, context.Request.Path, contentType);

    try
    {
        await next();
        logger.LogInformation("HTTP request completed: {Method} {Path} status={StatusCode} in {DurationMs}ms",
            context.Request.Method, context.Request.Path, context.Response.StatusCode, sw.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "HTTP request failed: {Method} {Path} after {DurationMs}ms",
            context.Request.Method, context.Request.Path, sw.ElapsedMilliseconds);
        throw;
    }
});

app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.MapGrpcService<ActivityGrpcService>().EnableGrpcWeb();
app.MapGet("/", () => "ActivitiesApp gRPC API is running. Use a gRPC client to communicate.");
app.MapGet("/diag/transport", () => Results.Ok(new
{
    mode = "grpc-web",
    port = 80,
    protocols = "http/1.1 + grpc-web"
}));

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("API started. DB provider: {DbProvider}. ApiAddress bound on port 80.", dbProvider);

app.Run();
