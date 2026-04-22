FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore
COPY src/ActivitiesApp.Api/ActivitiesApp.Api.csproj src/ActivitiesApp.Api/
COPY src/ActivitiesApp.Core/ActivitiesApp.Core.csproj src/ActivitiesApp.Core/
COPY src/ActivitiesApp.Infrastructure/ActivitiesApp.Infrastructure.csproj src/ActivitiesApp.Infrastructure/
COPY src/ActivitiesApp.Infrastructure.Migrations/ActivitiesApp.Infrastructure.Migrations.csproj src/ActivitiesApp.Infrastructure.Migrations/
COPY src/ActivitiesApp.ServiceDefaults/ActivitiesApp.ServiceDefaults.csproj src/ActivitiesApp.ServiceDefaults/
RUN dotnet restore src/ActivitiesApp.Api/ActivitiesApp.Api.csproj

# Copy everything and build
COPY src/ActivitiesApp.Api/ src/ActivitiesApp.Api/
COPY src/ActivitiesApp.Core/ src/ActivitiesApp.Core/
COPY src/ActivitiesApp.Infrastructure/ src/ActivitiesApp.Infrastructure/
COPY src/ActivitiesApp.Infrastructure.Migrations/ src/ActivitiesApp.Infrastructure.Migrations/
COPY src/ActivitiesApp.ServiceDefaults/ src/ActivitiesApp.ServiceDefaults/
RUN dotnet publish src/ActivitiesApp.Api/ActivitiesApp.Api.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ActivitiesApp.Api.dll"]
