# .NET 10 Project

A modern application built with **.NET 10**.

## Requirements

Before getting started, make sure you have:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git
- Your preferred IDE:
  - Visual Studio
  - Visual Studio Code
  - JetBrains Rider

Check your installed .NET version:

```bash
dotnet --version
```

## Getting Started

Clone the repository:

```bash
git clone https://github.com/your-username/your-project.git
cd your-project
```

Restore dependencies:

```bash
dotnet restore
```

Build the project:

```bash
dotnet build
```

Run the application:

```bash
dotnet run
```

## Development

Run in development mode:

```bash
dotnet run --environment Development
```

For projects that support hot reload:

```bash
dotnet watch run
```

## Testing

Run all tests:

```bash
dotnet test
```

Run tests with more detailed output:

```bash
dotnet test --verbosity normal
```

## Publishing

Publish a release build:

```bash
dotnet publish -c Release
```

Publish to a specific directory:

```bash
dotnet publish -c Release -o ./publish
```

## Configuration

Application configuration can be managed using:

- `appsettings.json`
- `appsettings.Development.json`
- Environment variables
- User Secrets for local development
- External secret/configuration providers for production

Example:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

For sensitive local configuration:

```bash
dotnet user-secrets init
dotnet user-secrets set "Example:ApiKey" "your-secret-value"
```

> Do not commit passwords, API keys, connection strings, or other secrets to source control.

## Project Structure

A typical solution might look like:

```text
.
├── src/
│   └── YourProject/
│       ├── Program.cs
│       ├── appsettings.json
│       └── YourProject.csproj
├── tests/
│   └── YourProject.Tests/
│       └── YourProject.Tests.csproj
├── YourProject.sln
└── README.md
```

## Common Commands

| Command | Description |
|---|---|
| `dotnet restore` | Restore NuGet packages |
| `dotnet build` | Build the project |
| `dotnet run` | Run the application |
| `dotnet watch run` | Run with hot reload |
| `dotnet test` | Run tests |
| `dotnet clean` | Remove build outputs |
| `dotnet publish -c Release` | Publish a release build |

## Docker

If the project includes a `Dockerfile`, build the image with:

```bash
docker build -t your-project .
```

Run it with:

```bash
docker run --rm -p 8080:8080 your-project
```

## Contributing

1. Create a new branch.
2. Make your changes.
3. Add or update tests where appropriate.
4. Run `dotnet build` and `dotnet test`.
5. Open a pull request.

## License

Add your project's license information here.

---

Built with **.NET 10**.