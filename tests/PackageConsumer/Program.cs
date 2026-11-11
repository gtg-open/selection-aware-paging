using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using GtG.Open.HC.SelectionAwarePaging;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using GtG.Open.HC.SelectionAwarePaging.PackageConsumer;

var libraryFramework = typeof(SelectionAwareOffsetPagingProvider).Assembly
    .GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
if (libraryFramework != AppContext.TargetFrameworkName)
{
    throw new InvalidOperationException(
        $"Expected package asset for {AppContext.TargetFrameworkName}, received {libraryFramework}.");
}

await using var services = new ServiceCollection()
    .AddGraphQLServer()
    .AddSelectionAwareOffsetPaging(defaultProvider: true)
    .AddQueryType<SampleQuery>()
    .ModifyPagingOptions(options => options.IncludeTotalCount = true)
    .Services.BuildServiceProvider();
var executor = await services.GetRequestExecutorAsync();
var result = await executor.ExecuteAsync(
    "{ numbers(skip: 1, take: 2) { items { id } totalCount pageInfo { hasNextPage hasPreviousPage } } }");
using var response = JsonDocument.Parse(result.ToJson());
if (response.RootElement.TryGetProperty("errors", out _))
{
    throw new InvalidOperationException(result.ToJson());
}

var page = response.RootElement.GetProperty("data").GetProperty("numbers");
var ids = page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt32());
if (!ids.SequenceEqual([2, 3]) || page.GetProperty("totalCount").GetInt32() != 5 ||
    !page.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean() ||
    !page.GetProperty("pageInfo").GetProperty("hasPreviousPage").GetBoolean())
{
    throw new InvalidOperationException("Package consumer returned an unexpected page.");
}
Console.WriteLine(
    $"Package consumer passed for {libraryFramework} on runtime {Environment.Version}: items, count and both page flags.");
