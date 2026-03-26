FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy csproj files and restore
COPY ActivitiesApp.Api/ActivitiesApp.Api.csproj ActivitiesApp.Api/
COPY ActivitiesApp.ServiceDefaults/ActivitiesApp.ServiceDefaults.csproj ActivitiesApp.ServiceDefaults/
RUN dotnet restore ActivitiesApp.Api/ActivitiesApp.Api.csproj

# Copy everything and build
COPY ActivitiesApp.Api/ ActivitiesApp.Api/
COPY ActivitiesApp.ServiceDefaults/ ActivitiesApp.ServiceDefaults/
RUN dotnet publish ActivitiesApp.Api/ActivitiesApp.Api.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ActivitiesApp.Api.dll"]
