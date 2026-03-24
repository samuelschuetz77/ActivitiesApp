FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY ActivitiesApp.Api/ActivitiesApp.Api.csproj ActivitiesApp.Api/
COPY ActivitiesApp.ServiceDefaults/ActivitiesApp.ServiceDefaults.csproj ActivitiesApp.ServiceDefaults/
RUN dotnet restore ActivitiesApp.Api/ActivitiesApp.Api.csproj

COPY ActivitiesApp.Api/ ActivitiesApp.Api/
COPY ActivitiesApp.ServiceDefaults/ ActivitiesApp.ServiceDefaults/
RUN dotnet publish ActivitiesApp.Api/ActivitiesApp.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

ENTRYPOINT ["dotnet", "ActivitiesApp.Api.dll"]
