using HotChocolate.Types;

namespace GtG.Open.HC.SelectionAwarePaging.PackageConsumer;

public sealed class SampleQuery
{
    [UseOffsetPaging]
    public IQueryable<SampleItem> GetNumbers() =>
        Enumerable.Range(1, 5).Select(id => new SampleItem(id)).AsQueryable();
}
