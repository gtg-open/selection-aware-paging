using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GtG.Open.HC.SelectionAwarePaging;

/// <summary>Registers selection-aware offset paging with Hot Chocolate.</summary>
public static class OffsetPagingRegistrationExtensions
{
    /// <summary>Adds a provider that executes only the paging work requested by the client.</summary>
    /// <param name="builder">The GraphQL request-executor builder.</param>
    /// <param name="providerName">An optional name used by field paging configuration.</param>
    /// <param name="defaultProvider">Whether this provider is the default for compatible fields.</param>
    /// <returns>The same builder for further configuration.</returns>
    public static IRequestExecutorBuilder AddSelectionAwareOffsetPaging(
        this IRequestExecutorBuilder builder,
        string? providerName = null,
        bool defaultProvider = false)
        => builder.AddOffsetPagingProvider<
            SelectionAwareOffsetPagingProvider>(
                providerName,
                defaultProvider);
}
