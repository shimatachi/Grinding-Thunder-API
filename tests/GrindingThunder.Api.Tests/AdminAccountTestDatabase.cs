using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GrindingThunder.Api.Tests;

/// <summary>
/// Shared in-memory SQLite database for admin account tests. When
/// <paramref name="sharedCacheName"/> is provided, multiple connections can
/// attach to the same in-memory database (used for concurrency tests).
/// </summary>
internal sealed class AdminAccountTestDatabase : IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly bool _shared;

    public AdminAccountTestDatabase(string? sharedCacheName = null)
    {
        _shared = sharedCacheName is not null;
        ConnectionString = _shared
            ? $"Data Source={sharedCacheName};Mode=Memory;Cache=Shared;Foreign Keys=True"
            : "Data Source=:memory:;Foreign Keys=True";

        _keepAliveConnection = new SqliteConnection(ConnectionString);
        _keepAliveConnection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public string ConnectionString { get; }

    public ApplicationDbContext CreateContext(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        // A plain ":memory:" database lives and dies with one connection, so
        // non-shared contexts must reuse the keep-alive connection. Shared-cache
        // mode allows additional connections to the same in-memory database.
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (_shared)
        {
            builder.UseSqlite(ConnectionString);
        }
        else
        {
            builder.UseSqlite(_keepAliveConnection);
        }

        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new ApplicationDbContext(builder.Options);
    }

    public void Dispose() => _keepAliveConnection.Dispose();
}
