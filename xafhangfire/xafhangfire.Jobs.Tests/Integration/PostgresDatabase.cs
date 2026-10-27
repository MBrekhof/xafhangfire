using Microsoft.EntityFrameworkCore;
using Npgsql;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Jobs.Tests.Integration;

// TEST-001: a throwaway database in the docker-compose Postgres (never the dev database `xafhangfire`).
// Dropped and recreated at the start of every run; left in place afterwards for inspection.
public sealed class PostgresDatabase
{
    public const int Port = 5433;
    public const string Name = "xafhangfire_inttest";
    public static readonly string ConnectionString =
        $"Host=localhost;Port={Port};Database={Name};Username=xafhangfire;Password=xafhangfire";

    // A real login, not a TCP connect: a port can accept connections before Postgres is ready, and a TcpClient
    // probe of "localhost" timed out on ::1 and missed a Postgres listening on IPv4 only.
    public static readonly Lazy<bool> IsAvailable = new(() =>
    {
        var probe = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres", Pooling = false, Timeout = 3 };
        try
        {
            using var connection = new NpgsqlConnection(probe.ConnectionString);
            connection.Open();
            return true;
        }
        // Postgres answered: run (a wrong password then fails loudly in the fixture), unless it is still starting up.
        catch (PostgresException e) { return e.SqlState != PostgresErrorCodes.CannotConnectNow; }
        catch (Exception) { return false; } // nothing speaking Postgres on the port
    });

    public PostgresDatabase()
    {
        // xunit builds collection fixtures even when every test in the collection is skipped.
        if (!IsAvailable.Value) return;
        using var context = CreateContext();
        var target = context.Database.GetDbConnection().Database;
        if (target != Name)
            throw new InvalidOperationException($"Refusing to drop database '{target}'; only '{Name}' is a test database.");
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();
    }

    // Built the way XAF builds it: UseConnectionString adds Npgsql plus the change-tracking proxies.
    public xafhangfireEFCoreDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<xafhangfireEFCoreDbContext>();
        options.UseConnectionString("EFCoreProvider=PostgreSql;" + ConnectionString);
        return new xafhangfireEFCoreDbContext(options.Options);
    }

    public static object? Scalar(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command.ExecuteScalar();
    }
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresDatabase>;

// Skipped, not failed, when Postgres isn't running (docker compose up -d).
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!PostgresDatabase.IsAvailable.Value)
            Skip = $"Postgres on localhost:{PostgresDatabase.Port} is not reachable; run docker compose up -d";
    }
}
