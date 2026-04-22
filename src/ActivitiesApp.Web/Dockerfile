FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore
COPY src/ActivitiesApp.Web/ActivitiesApp.Web.csproj src/ActivitiesApp.Web/
COPY src/ActivitiesApp.Core/ActivitiesApp.Core.csproj src/ActivitiesApp.Core/
COPY src/ActivitiesApp.Shared/ActivitiesApp.Shared.csproj src/ActivitiesApp.Shared/
COPY src/ActivitiesApp.ServiceDefaults/ActivitiesApp.ServiceDefaults.csproj src/ActivitiesApp.ServiceDefaults/
COPY src/ActivitiesApp.Api/Protos/ src/ActivitiesApp.Api/Protos/
RUN dotnet restore src/ActivitiesApp.Web/ActivitiesApp.Web.csproj

# Copy everything and build
COPY src/ActivitiesApp.Web/ src/ActivitiesApp.Web/
COPY src/ActivitiesApp.Core/ src/ActivitiesApp.Core/
COPY src/ActivitiesApp.Shared/ src/ActivitiesApp.Shared/
COPY src/ActivitiesApp.ServiceDefaults/ src/ActivitiesApp.ServiceDefaults/
COPY src/ActivitiesApp.Api/Protos/ src/ActivitiesApp.Api/Protos/
RUN dotnet publish src/ActivitiesApp.Web/ActivitiesApp.Web.csproj -c Release -o /app/publish --no-restore
RUN mkdir -p /app/publish/wwwroot/_framework \
    && cp src/ActivitiesApp.Web/DockerAssets/_framework/blazor.server.js /app/publish/wwwroot/_framework/blazor.server.js \
    && cp src/ActivitiesApp.Web/DockerAssets/_framework/blazor.web.js /app/publish/wwwroot/_framework/blazor.web.js

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ActivitiesApp.Web.dll"]
