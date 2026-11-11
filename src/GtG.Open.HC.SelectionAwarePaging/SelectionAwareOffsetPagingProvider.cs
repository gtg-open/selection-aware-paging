using System;
using System.Reflection;
using HotChocolate.Internal;
using HotChocolate.Types.Pagination;

namespace GtG.Open.HC.SelectionAwarePaging;

/// <summary>
/// Creates offset-paging handlers that execute only the work required by the
/// effective GraphQL selection.
/// </summary>
public sealed class SelectionAwareOffsetPagingProvider
    : QueryableOffsetPagingProvider
{
    private static readonly MethodInfo s_createHandler =
        typeof(SelectionAwareOffsetPagingProvider).GetMethod(
            nameof(CreateHandlerInternal),
            BindingFlags.Static | BindingFlags.NonPublic)!;

    /// <inheritdoc />
    protected override OffsetPagingHandler CreateHandler(
        IExtendedType source,
        PagingOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);

        return (OffsetPagingHandler)s_createHandler
            .MakeGenericMethod(source.ElementType?.Source ?? source.Source)
            .Invoke(null, [options])!;
    }

    private static SelectionAwareOffsetPagingHandler<TEntity>
        CreateHandlerInternal<TEntity>(PagingOptions options) => new(options);
}
