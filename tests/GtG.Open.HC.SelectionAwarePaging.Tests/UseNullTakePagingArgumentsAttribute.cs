using System.Reflection;
using System.Runtime.CompilerServices;
using HotChocolate;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Pagination;

namespace GtG.Open.HC.SelectionAwarePaging.Tests;

public sealed class UseNullTakePagingArgumentsAttribute : ObjectFieldDescriptorAttribute
{
    public UseNullTakePagingArgumentsAttribute(
        [CallerLineNumber] int order = 0)
    {
        Order = order;
    }

    protected override void OnConfigure(
        IDescriptorContext context,
        IObjectFieldDescriptor descriptor,
        MemberInfo? member)
    {
        // Keep this attribute after UseOffsetPaging: the resulting inner
        // middleware must replace the defaulted arguments before slicing.
        descriptor.Use(next => async middlewareContext =>
        {
            middlewareContext.SetLocalState(
                WellKnownContextData.PagingArguments,
                new OffsetPagingArguments(
                    middlewareContext.ArgumentValue<int?>("skip"),
                    null));

            await next(middlewareContext);
        });
    }
}
