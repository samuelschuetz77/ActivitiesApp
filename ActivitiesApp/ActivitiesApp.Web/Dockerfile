FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY ActivitiesApp/ActivitiesApp.Web/ActivitiesApp.Web.csproj ActivitiesApp/ActivitiesApp.Web/
COPY ActivitiesApp/ActivitiesApp.Shared/ActivitiesApp.Shared.csproj ActivitiesApp/ActivitiesApp.Shared/
COPY ActivitiesApp.ServiceDefaults/ActivitiesApp.ServiceDefaults.csproj ActivitiesApp.ServiceDefaults/
COPY ActivitiesApp.Api/ActivitiesApp.Api.csproj ActivitiesApp.Api/
RUN dotnet restore ActivitiesApp/ActivitiesApp.Web/ActivitiesApp.Web.csproj

COPY ActivitiesApp/ActivitiesApp.Web/ ActivitiesApp/ActivitiesApp.Web/
COPY ActivitiesApp/ActivitiesApp.Shared/ ActivitiesApp/ActivitiesApp.Shared/
COPY ActivitiesApp.ServiceDefaults/ ActivitiesApp.ServiceDefaults/
COPY ActivitiesApp.Api/ ActivitiesApp.Api/
RUN dotnet publish ActivitiesApp/ActivitiesApp.Web/ActivitiesApp.Web.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

ENTRYPOINT ["dotnet", "ActivitiesApp.Web.dll"]
