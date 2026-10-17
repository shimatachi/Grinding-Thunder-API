FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/GrindingThunder.Api/GrindingThunder.Api.csproj src/GrindingThunder.Api/
RUN dotnet restore src/GrindingThunder.Api/GrindingThunder.Api.csproj

COPY src/GrindingThunder.Api/ src/GrindingThunder.Api/
RUN dotnet publish src/GrindingThunder.Api/GrindingThunder.Api.csproj \
    --configuration Release \
    --no-restore \
    --property:UseAppHost=false \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "GrindingThunder.Api.dll"]
