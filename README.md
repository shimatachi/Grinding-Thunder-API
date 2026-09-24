# Grinding Thunder API

ASP.NET Core backend for planning War Thunder vehicle progression. It models vehicle prerequisites and rank gates so a player can estimate the remaining Research Points (RP) and matches needed to reach a target vehicle.

## Overview

Grinding Thunder is intended to answer a practical progression question: given a player's unlocked vehicles and a target vehicle, what still needs to be researched?

The current API exposes nation and vehicle data, traverses explicit prerequisite links, includes player-selected vehicles needed toward rank gates, totals remaining RP, and estimates matches from average RP earned per match. It is an early prototype: it does not yet persist player inventories, calculate total Silver Lions (SL), select rank-gate fillers automatically, or provide historical tech-tree versions.

## Tech Stack

- .NET 10 and ASP.NET Core Web API
- Entity Framework Core 10
- Npgsql EF Core provider
- PostgreSQL
- ASP.NET Core OpenAPI document generation in Development
- Docker multi-stage build using the .NET 10 SDK and ASP.NET Core runtime images

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PostgreSQL accessible to the application
- Docker, only if building or running the container image

## Getting Started

1. Restore and build the project:

   ```console
   dotnet restore GrindingThunder.Api.csproj
   dotnet build GrindingThunder.Api.csproj --no-restore
   ```

2. Configure a PostgreSQL connection. The application reads `ConnectionStrings:DefaultConnection`. Prefer overriding the checked-in local default with an environment variable or .NET user secrets:

   ```console
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=warthunder_db;Username=postgres;Password=YOUR_PASSWORD" --project GrindingThunder.Api.csproj
   ```

3. Run the Development launch profile:

   ```console
   dotnet run --project GrindingThunder.Api.csproj --launch-profile http
   ```

   The profile listens on `http://localhost:5034`. On startup, the application connects to PostgreSQL, applies its EF Core migrations, and seeds the development sample data. The PostgreSQL user must have the permissions required to create or update the configured database schema.

4. With the application running, the Development OpenAPI document is available at `http://localhost:5034/openapi/v1.json`.

## Configuration

Configuration follows standard ASP.NET Core configuration precedence. The repository defines these application-specific settings:

Work in Progress

## Development

Run with automatic rebuild and hot reload:

```console
dotnet watch --project GrindingThunder.Api.csproj run --launch-profile http
```

Create a release build:

```console
dotnet build GrindingThunder.Api.csproj -c Release
```

Build the provided container image:

```console
docker build -t grinding-thunder-api .
```

The image listens on port `8080`. A running container still requires a reachable PostgreSQL instance and an appropriate `ConnectionStrings__DefaultConnection` value. Migration and seed startup behavior occurs only when the container environment is `Development`.

## Testing

There is currently no automated test project, lint script, or CI workflow in this repository. Use a successful build as the available compile-time check:

```console
dotnet build GrindingThunder.Api.csproj
```

## Project Structure

```text
.
|-- Application/                 # Calculation request and result models
|-- Controllers/                 # HTTP API controllers
|-- Domain/                      # Entities and calculator contract
|-- Infrastructure/
|   |-- Persistence/             # DbContext, mappings, migrations, and seed data
|   `-- Services/                # Research calculator implementation
|-- Program.cs                   # Application composition and middleware
|-- GrindingThunder.Api.csproj   # Project and NuGet dependencies
`-- Dockerfile                   # Production image build
```
