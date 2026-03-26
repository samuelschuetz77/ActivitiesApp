FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy csproj files and restore
COPY ActivitiesApp/ActivitiesApp.Web/ActivitiesApp.Web.csproj ActivitiesApp/ActivitiesApp.Web/
COPY ActivitiesApp/ActivitiesApp.Shared/ActivitiesApp.Shared.csproj ActivitiesApp/ActivitiesApp.Shared/
COPY ActivitiesApp.ServiceDefaults/ActivitiesApp.ServiceDefaults.csproj ActivitiesApp.ServiceDefaults/
COPY ActivitiesApp.Api/Protos/ ActivitiesApp.Api/Protos/
RUN dotnet restore ActivitiesApp/ActivitiesApp.Web/ActivitiesApp.Web.csproj

# Copy everything and build
COPY ActivitiesApp/ActivitiesApp.Web/ ActivitiesApp/ActivitiesApp.Web/
COPY ActivitiesApp/ActivitiesApp.Shared/ ActivitiesApp/ActivitiesApp.Shared/
COPY ActivitiesApp.ServiceDefaults/ ActivitiesApp.ServiceDefaults/
COPY ActivitiesApp.Api/Protos/ ActivitiesApp.Api/Protos/
RUN dotnet publish ActivitiesApp/ActivitiesApp.Web/ActivitiesApp.Web.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ActivitiesApp.Web.dll"]
