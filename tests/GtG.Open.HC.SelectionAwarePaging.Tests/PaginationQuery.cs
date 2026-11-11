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

public sealed class PaginationQuery
{
    public const string SelectionAwareProviderName = "selection-aware";

    [UseOffsetPaging]
    [UseFiltering]
    [UseSorting]
    public IQueryable<TrackedItem> GetItems([Service] DataConnection db) =>
        db.GetTable<TrackedItem>().OrderBy(item => item.Id);

    [UseOffsetPaging(ProviderName = SelectionAwareProviderName)]
    public IQueryable<TrackedItem> GetNamedItems([Service] DataConnection db) =>
        db.GetTable<TrackedItem>().OrderBy(item => item.Id);

    [UseOffsetPaging]
    [UseSorting]
    public IQueryable<TrackedItem> GetSortableItems([Service] DataConnection db) =>
        db.GetTable<TrackedItem>();

    [UseOffsetPaging]
    public IQueryable<TrackedItem> GetObservedItems([Service] DataConnection db) =>
        db.GetTable<TrackedItem>().OrderBy(item => item.Id);

    [UseOffsetPaging]
    [UseNullTakePagingArguments]
    public IQueryable<TrackedItem> GetUnboundedItems([Service] DataConnection db) =>
        db.GetTable<TrackedItem>().OrderBy(item => item.Id);

    [UseOffsetPaging]
    public IEnumerable<MemoryItem> GetMemoryItems() =>
        Enumerable.Range(1, 5).Select(id => new MemoryItem(id, $"memory-{id}")).ToArray();

    [UseOffsetPaging]
    public IExecutable<MemoryItem> GetExecutableItems() =>
        new TestExecutable<MemoryItem>(
            Enumerable.Range(1, 5)
                .Select(id => new MemoryItem(id, $"executable-{id}"))
                .AsQueryable());
}
