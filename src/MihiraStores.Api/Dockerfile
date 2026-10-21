# Optional: containerised API. Build from the repository root:
#   docker build -f src/MihiraStores.Api/Dockerfile -t mihira-api .
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/MihiraStores.Api/MihiraStores.Api.csproj", "src/MihiraStores.Api/"]
RUN dotnet restore "src/MihiraStores.Api/MihiraStores.Api.csproj"
COPY src/ src/
RUN dotnet publish "src/MihiraStores.Api/MihiraStores.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MihiraStores.Api.dll"]
