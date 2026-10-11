# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /app

# Copy csproj and restore dependencies
COPY src/GrindingThunder.Api/GrindingThunder.Api.csproj src/GrindingThunder.Api/
RUN dotnet restore src/GrindingThunder.Api/GrindingThunder.Api.csproj

# Copy source code and publish
COPY src/GrindingThunder.Api/ src/GrindingThunder.Api/
RUN dotnet publish src/GrindingThunder.Api/GrindingThunder.Api.csproj -c Release -o /app/out

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Copy published output
COPY --from=build-env /app/out .

# Expose port and configure ASP.NET Core
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "GrindingThunder.Api.dll"]
