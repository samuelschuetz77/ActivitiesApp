FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY src/ActivitiesApp.Infrastructure.Migrations/ActivitiesApp.Infrastructure.Migrations.csproj src/ActivitiesApp.Infrastructure.Migrations/
COPY src/ActivitiesApp.Infrastructure/ActivitiesApp.Infrastructure.csproj src/ActivitiesApp.Infrastructure/
COPY src/ActivitiesApp.Shared/ActivitiesApp.Shared.csproj src/ActivitiesApp.Shared/
RUN dotnet restore src/ActivitiesApp.Infrastructure.Migrations/ActivitiesApp.Infrastructure.Migrations.csproj

COPY src/ActivitiesApp.Infrastructure.Migrations/ src/ActivitiesApp.Infrastructure.Migrations/
COPY src/ActivitiesApp.Infrastructure/ src/ActivitiesApp.Infrastructure/
COPY src/ActivitiesApp.Shared/ src/ActivitiesApp.Shared/
RUN dotnet publish src/ActivitiesApp.Infrastructure.Migrations/ActivitiesApp.Infrastructure.Migrations.csproj \
    -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ActivitiesApp.Infrastructure.Migrations.dll"]
