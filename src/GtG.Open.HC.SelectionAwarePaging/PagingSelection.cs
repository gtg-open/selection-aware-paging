using System.Collections.Generic;
using System.Collections.Immutable;
using HotChocolate;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Pagination;

namespace GtG.Open.HC.SelectionAwarePaging;

internal readonly record struct PagingSelection(
    bool NeedsItems,
    bool NeedsTotalCount,
    bool NeedsHasNextPage)
{
    public static PagingSelection Analyze(
        IResolverContext context,
        bool includeTotalCount)
    {
        var hasObserver = context.LocalContextData.TryGetValue(
                WellKnownContextData.PagingObserver,
                out var observerValue) &&
            HasPageObservers(observerValue);
        var needsItems = hasObserver;

        // GetSelections requires a concrete ObjectType. Unlike IsSelected
        // and Select below, it cannot resolve abstract types or safely
        // ignore non-composite types, so guard before passing the type.
        if (context.Selection.Type.NamedType() is not ObjectType segmentType)
        {
            // Unknown result shape: retain upstream materialization behavior.
            needsItems = true;
        }
        else
        {
            foreach (var child in context.GetSelections(segmentType))
            {
                // Treat every custom segment field as item-dependent. For example,
                // it may sum values from CollectionSegment.Items even when the client
                // does not select the items field. Page metadata is computed only when
                // selected or observed; custom fields are not expected to read
                // CollectionSegment.Info.
                if (child.Field.Name is not
                    ("totalCount" or "pageInfo" or "__typename"))
                {
                    needsItems = true;
                    break;
                }
            }
        }

        var needsTotalCount = includeTotalCount &&
            context.IsSelected("totalCount");
        var needsHasNextPage = hasObserver ||
            context.Select("pageInfo").IsSelected("hasNextPage");

        return new PagingSelection(
            needsItems,
            needsTotalCount,
            needsHasNextPage);
    }

    private static bool HasPageObservers(object? value) =>
        value switch
        {
            // Hot Chocolate 16.6 stores observers as an ImmutableArray. Check it first
            // because Count throws for a default ImmutableArray.
            ImmutableArray<IPageObserver> observers =>
                !observers.IsDefaultOrEmpty,
            // Retain compatibility if Hot Chocolate later uses another read-only collection.
            IReadOnlyCollection<IPageObserver> observers =>
                observers.Count > 0,
            _ => false
        };
}
