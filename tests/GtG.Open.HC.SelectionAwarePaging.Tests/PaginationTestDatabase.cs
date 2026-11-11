using System.Diagnostics;
using System.Net;
using LinqToDB;
using LinqToDB.Data;
using Npgsql;

namespace GtG.Open.HC.SelectionAwarePaging.Tests;

public static class PaginationTestDatabase
{
    public const string ConnectionStringEnvironmentVariable =
        "PAGING_TEST_POSTGRES_CONNECTION";

    private const string RequiredDatabase = "paging_tests";

    public static DataConnection CreateConnection(DataOptions options, SqlTrace trace)
    {
        var db = new DataConnection(options);
        DataConnection.TurnTraceSwitchOn(TraceLevel.Info);
        db.OnTraceConnection = trace.Capture;
        return db;
    }

    public static async Task<PostgreSqlTestDatabase> CreateSeededAsync(SqlTrace trace)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringEnvironmentVariable} must point to the local PostgreSQL test database.");
        }

        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        ValidateTestDatabase(connectionStringBuilder);
        connectionStringBuilder.Pooling = false;
        connectionStringBuilder.ApplicationName = "GtG.Open.HC.SelectionAwarePaging.Tests";
        connectionStringBuilder.SearchPath = string.Empty;

        var schemaName = $"pagination_{Guid.NewGuid():N}";
        var adminOptions = new DataOptions().UsePostgreSQL(
            connectionStringBuilder.ConnectionString);

        await using (var admin = new DataConnection(adminOptions))
        {
            await admin.ExecuteAsync($"CREATE SCHEMA \"{schemaName}\"");
        }

        connectionStringBuilder.SearchPath = schemaName;
        var database = new PostgreSqlTestDatabase(
            adminOptions,
            new DataOptions().UsePostgreSQL(connectionStringBuilder.ConnectionString),
            schemaName);

        try
        {
            await using var db = CreateConnection(database.DataOptions, trace);
            await db.ExecuteAsync(
                "CREATE TABLE \"Items\" (\"Id\" integer PRIMARY KEY, \"Name\" text NOT NULL)");

            foreach (var id in Enumerable.Range(1, 5))
            {
                await db.InsertAsync(new TrackedItem { Id = id, Name = $"item-{id}" });
            }

            trace.Clear();
            TrackedItem.ResetConstructionCount();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static void ValidateTestDatabase(
        NpgsqlConnectionStringBuilder connectionStringBuilder)
    {
        var isLoopback = string.Equals(
                connectionStringBuilder.Host,
                "localhost",
                StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(connectionStringBuilder.Host, out var address) &&
            IPAddress.IsLoopback(address);

        if (!isLoopback)
        {
            throw new InvalidOperationException(
                "PostgreSQL pagination tests may connect only through a loopback address.");
        }

        if (!string.Equals(
                connectionStringBuilder.Database,
                RequiredDatabase,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"PostgreSQL pagination tests require the dedicated '{RequiredDatabase}' database.");
        }
    }
}
