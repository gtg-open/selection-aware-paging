using System.Diagnostics;
using System.Net;
using LinqToDB;
using LinqToDB.Data;
using Npgsql;

namespace GtG.Open.HC.SelectionAwarePaging.Tests;

public sealed class PostgreSqlTestDatabase(
    DataOptions adminOptions,
    DataOptions dataOptions,
    string schemaName) : IAsyncDisposable
{
    private int _disposed;

    public DataOptions DataOptions { get; } = dataOptions;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await using var admin = new DataConnection(adminOptions);
        await admin.ExecuteAsync($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE");
    }
}
