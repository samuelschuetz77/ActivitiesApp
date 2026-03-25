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
builder.Services.AddHttpClient<GooglePlacesService>();

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await context.Database.EnsureCreatedAsync();
}

app.MapDefaultEndpoints();

app.MapGrpcService<ActivityGrpcService>();
app.MapGet("/", () => "ActivitiesApp gRPC API is running. Use a gRPC client to communicate.");

app.Run();
