var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.ActivitiesApp>("activitiesapp");

builder.AddProject<Projects.ActivitiesApp_Web>("activitiesapp-web");

builder.Build().Run();
