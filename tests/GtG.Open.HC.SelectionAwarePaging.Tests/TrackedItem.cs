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

[Table("Items")]
public sealed class TrackedItem
{
    private static int _constructionCount;

    public TrackedItem() => Interlocked.Increment(ref _constructionCount);

    public static int ConstructionCount => Volatile.Read(ref _constructionCount);

    public static void ResetConstructionCount() => Interlocked.Exchange(ref _constructionCount, 0);

    [PrimaryKey]
    [Column]
    public int Id { get; set; }

    [Column]
    public string Name { get; set; } = string.Empty;
}
