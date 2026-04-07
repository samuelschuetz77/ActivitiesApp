var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.ActivitiesApp_Api>("activitiesapp-api");

builder.AddProject<Projects.ActivitiesApp>("activitiesapp")
    .WithReference(api);

builder.AddProject<Projects.ActivitiesApp_Web>("activitiesapp-web")
    .WithReference(api);

builder.Build().Run();
