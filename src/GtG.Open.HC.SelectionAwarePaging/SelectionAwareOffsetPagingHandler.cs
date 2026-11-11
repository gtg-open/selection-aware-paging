using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotChocolate;
using HotChocolate.Internal;
using HotChocolate.Resolvers;
using HotChocolate.Types.Pagination;

namespace GtG.Open.HC.SelectionAwarePaging;

/// <summary>
/// Replaces the non-extensible execution part of Hot Chocolate's offset-paging
/// implementation. The built-in algorithm always materializes page items and
/// requests one lookahead row, even when the selection needs only totalCount,
/// __typename, or hasPreviousPage. This handler examines the fields requested
/// for the current response and runs only the item, count, or next-page queries
/// needed for those fields. It still returns the standard CollectionSegment
/// result, so the schema and response shape do not change.
/// </summary>
internal sealed class SelectionAwareOffsetPagingHandler<TEntity>(
    PagingOptions options)
    : QueryableOffsetPagingHandler<TEntity>(options)
{
    protected override ValueTask<CollectionSegment> SliceAsync(
        IResolverContext context,
        object source,
        OffsetPagingArguments arguments)
    {
        // PagingMiddleware supplies Take for normal requests. At handler level,
        // QueryableOffsetPagingHandler treats a null Take as unbounded, so keep
        // it in the custom path and delegate only unsupported negative values.
        if (arguments.Take is < 0 ||
            arguments.Skip is < 0)
        {
            return base.SliceAsync(context, source, arguments);
        }

        var selection = PagingSelection.Analyze(context, IncludeTotalCount);

        return source switch
        {
            IQueryable<TEntity> query =>
                ResolveAsync(context, query, arguments.Skip, arguments.Take, selection),
            // EnumerableQuery cannot compose Skip/Take when the enumerable's
            // runtime type is a value type (for example ImmutableArray<T>):
            // its expression contains the struct where an IEnumerable<T> is
            // required, but no boxing conversion. Fall through to base so Hot
            // Chocolate returns its stable unsupported-source GraphQL error.
            IEnumerable<TEntity> enumerable when !enumerable.GetType().IsValueType =>
                ResolveAsync(
                    context,
                    enumerable.AsQueryable(),
                    arguments.Skip,
                    arguments.Take,
                    selection),
            IExecutable<TEntity> executable =>
                SliceAsync(context, executable.Source, arguments),
            _ => base.SliceAsync(context, source, arguments)
        };
    }

    private static async ValueTask<CollectionSegment> ResolveAsync(
        IResolverContext context,
        IQueryable<TEntity> query,
        int? skip,
        int? take,
        PagingSelection selection)
    {
        var cancellationToken = context.RequestAborted;
        var effectiveSkip = skip ?? 0;
        var needsItems = selection.NeedsItems && take != 0;
        var needsHasNextPage = selection.NeedsHasNextPage && take != null;
        var totalCount = -1;
        var hasNextPage = false;
        IReadOnlyList<TEntity> pageItems = [];

        if (needsItems)
        {
            var sliced = query;

            if (skip is { } skipValue)
            {
                sliced = sliced.Skip(skipValue);
            }

            var useLookahead = false;
            if (take != null)
            {
                // Queryable.Take accepts only int. Lookahead requires take + 1,
                // so it cannot be used when take is already int.MaxValue.
                useLookahead = needsHasNextPage &&
                    !selection.NeedsTotalCount &&
                    take.Value < int.MaxValue;

                // Fetch an extra row only when hasNextPage cannot be derived
                // from totalCount; otherwise request exactly the page size.
                sliced = sliced.Take(
                    useLookahead ? take.Value + 1 : take.Value);
            }

            var items = await ExecuteAsync(sliced, cancellationToken)
                .ConfigureAwait(false);

            if (useLookahead && items.Count == take!.Value + 1)
            {
                hasNextPage = true;
                items.RemoveAt(items.Count - 1);
            }

            pageItems = items;
        }

        if (selection.NeedsTotalCount)
        {
            totalCount = await Task.Run(query.Count, cancellationToken)
                .ConfigureAwait(false);

            if (needsHasNextPage)
            {
                // Add as long so skip + take cannot overflow before comparison.
                // totalCount is promoted from int; a boundary above int.MaxValue
                // therefore correctly means that no next page can exist.
                hasNextPage = (long)effectiveSkip + take!.Value < totalCount;
            }
        }
        else if (needsHasNextPage &&
                 (!needsItems || take == int.MaxValue))
        {
            // pageInfo alone needs existence after the requested page, not the
            // requested page's rows.
            var afterPageOffset = (long)effectiveSkip + take!.Value;
            if (afterPageOffset <= int.MaxValue)
            {
                var afterPage = query.Skip((int)afterPageOffset);
                hasNextPage = await Task.Run(
                        () => afterPage.Any(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return new CollectionSegment<TEntity>(
            pageItems,
            new CollectionSegmentInfo(hasNextPage, effectiveSkip > 0),
            totalCount);
    }

    private static async ValueTask<List<TEntity>> ExecuteAsync(
        IQueryable<TEntity> query,
        CancellationToken cancellationToken)
    {
        var items = new List<TEntity>();

        if (query is IAsyncEnumerable<TEntity> asyncEnumerable)
        {
            await foreach (var item in asyncEnumerable
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                items.Add(item);
            }
        }
        else
        {
            await Task.Run(
                    () =>
                    {
                        foreach (var item in query)
                        {
                            // Mirror Hot Chocolate's synchronous fallback: stop
                            // enumeration without throwing from inside the loop.
                            if (cancellationToken.IsCancellationRequested)
                            {
                                break;
                            }

                            items.Add(item);
                        }
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return items;
    }
}
