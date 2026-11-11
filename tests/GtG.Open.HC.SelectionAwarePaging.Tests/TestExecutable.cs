using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HotChocolate;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Pagination;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using LinqToDB.SqlQuery;

namespace GtG.Open.HC.SelectionAwarePaging.Tests;

public sealed class TestExecutable<T>(IQueryable<T> source) : IExecutable<T>
{
    public object Source => source;

    ValueTask<IList> IExecutable.ToListAsync(CancellationToken cancellationToken) =>
        new(source.ToList());

    public ValueTask<List<T>> ToListAsync(CancellationToken cancellationToken = default) =>
        new(source.ToList());

    public async IAsyncEnumerable<T> ToAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    async IAsyncEnumerable<object?> IExecutable.ToAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in ToAsyncEnumerable(cancellationToken))
        {
            yield return item;
        }
    }

    ValueTask<object?> IExecutable.FirstOrDefaultAsync(CancellationToken cancellationToken) =>
        new(source.FirstOrDefault());

    public ValueTask<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default) =>
        new(source.FirstOrDefault());

    ValueTask<object?> IExecutable.SingleOrDefaultAsync(CancellationToken cancellationToken) =>
        new(source.SingleOrDefault());

    public ValueTask<T?> SingleOrDefaultAsync(CancellationToken cancellationToken = default) =>
        new(source.SingleOrDefault());

    public ValueTask<int> CountAsync(CancellationToken cancellationToken = default) =>
        new(source.Count());

    public string Print() => source.ToString() ?? string.Empty;
}
