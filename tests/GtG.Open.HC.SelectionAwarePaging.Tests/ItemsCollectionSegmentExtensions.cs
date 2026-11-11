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

[ExtendObjectType("ItemsCollectionSegment")]
public sealed class ItemsCollectionSegmentExtensions
{
    public int SumOfIdsOnPage(IResolverContext context) =>
        context.Parent<CollectionSegment>().Items.Cast<TrackedItem>().Sum(item => item.Id);
}
