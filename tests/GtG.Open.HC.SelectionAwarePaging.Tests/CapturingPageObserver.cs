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

public sealed class CapturingPageObserver : IPageObserver
{
    public IReadOnlyList<int> ItemIds { get; private set; } = [];

    public bool HasNextPage { get; private set; }

    public bool HasPreviousPage { get; private set; }

    public void OnAfterSliced<T>(ReadOnlySpan<T> items, IPageInfo pageInfo)
    {
        var itemIds = new int[items.Length];

        for (var index = 0; index < items.Length; index++)
        {
            itemIds[index] = ((TrackedItem)(object)items[index]!).Id;
        }

        ItemIds = itemIds;
        HasNextPage = pageInfo.HasNextPage;
        HasPreviousPage = pageInfo.HasPreviousPage;
    }
}
