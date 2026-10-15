# Read-only real-data audit followed by disposable, transaction-isolated PostgreSQL fixtures.
# Does not apply Batch 23 to the real development schema.
$ErrorActionPreference = 'Stop'
$apiDirectory = (Resolve-Path (Join-Path $PSScriptRoot '../../src/GrindingThunder.Api')).Path
$verificationDirectory = Join-Path $PSScriptRoot 'obj/batch23-postgres-verification'
New-Item -ItemType Directory -Path $verificationDirectory -Force | Out-Null
$testProject = [System.Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'GrindingThunder.Api.Tests.csproj'))
$projectText = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup><ProjectReference Include="$testProject" /></ItemGroup>
</Project>
"@
Set-Content -LiteralPath (Join-Path $verificationDirectory 'PostgresVerification.csproj') -Value $projectText
Set-Content -LiteralPath (Join-Path $verificationDirectory 'Program.cs') -Value 'await GrindingThunder.Api.Tests.VehicleOwnershipPostgresVerification.RunAsync(args[0]);'
dotnet run --project (Join-Path $verificationDirectory 'PostgresVerification.csproj') -- $apiDirectory
if ($LASTEXITCODE -ne 0) { throw 'Vehicle ownership PostgreSQL verification failed; see the exact blocker above.' }
