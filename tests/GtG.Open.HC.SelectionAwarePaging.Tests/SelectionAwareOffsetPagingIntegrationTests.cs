using System.Text.Json;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types.Pagination;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Is = NUnit.Framework.Is;

namespace GtG.Open.HC.SelectionAwarePaging.Tests;

[TestFixture]
[NonParallelizable]
public sealed class SelectionAwareOffsetPagingIntegrationTests
{
    private PostgreSqlTestDatabase _database = null!;
    private DataOptions _dataOptions = null!;
    private SqlTrace _trace = null!;
    private ServiceProvider _services = null!;
    private IRequestExecutor _executor = null!;

    [OneTimeSetUp]
    public async Task SetUpFixture()
    {
        _trace = new SqlTrace();
        _database = await PaginationTestDatabase.CreateSeededAsync(_trace);

        try
        {
            _dataOptions = _database.DataOptions;
            _services = CreateServices(useSelectionAwareProvider: true);
            _executor = await _services.GetRequestExecutorAsync();
        }
        catch
        {
            if (_services is not null)
            {
                await _services.DisposeAsync();
            }

            await _database.DisposeAsync();
            throw;
        }
    }

    [OneTimeTearDown]
    public async Task TearDownFixture()
    {
        try
        {
            if (_services is not null)
            {
                await _services.DisposeAsync();
            }
        }
        finally
        {
            if (_database is not null)
            {
                await _database.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task TotalCountOnly_ExecutesOnlyCountWithoutMaterializingItems()
    {
        using var response = await ExecuteAsync("{ items { totalCount } }");

        Assert.Multiple(() =>
        {
            Assert.That(GetData(response, "items").GetProperty("totalCount").GetInt32(), Is.EqualTo(5));
            Assert.That(TrackedItem.ConstructionCount, Is.Zero);
            AssertCountOnlyCommand();
        });
    }

    [Test]
    public async Task NamedRegistration_ForwardsProviderName()
    {
        await using var services = CreateServices(
            useSelectionAwareProvider: false,
            configureProviders: graphQl =>
            {
                graphQl.AddQueryableOffsetPagingProvider(defaultProvider: true);
                graphQl.AddSelectionAwareOffsetPaging(
                    PaginationQuery.SelectionAwareProviderName);
            });
        var executor = await services.GetRequestExecutorAsync();

        using var response = await ExecuteAsync(
            executor,
            "{ namedItems { totalCount } }");

        Assert.Multiple(() =>
        {
            Assert.That(
                GetData(response, "namedItems").GetProperty("totalCount").GetInt32(),
                Is.EqualTo(5));
            Assert.That(TrackedItem.ConstructionCount, Is.Zero);
            AssertCountOnlyCommand();
        });
    }

    [Test]
    public async Task DefaultRegistration_ForwardsDefaultProvider()
    {
        await using var services = CreateServices(
            useSelectionAwareProvider: false,
            configureProviders: graphQl =>
            {
                graphQl.AddQueryableOffsetPagingProvider(defaultProvider: true);
                graphQl.AddSelectionAwareOffsetPaging(defaultProvider: true);
            });
        var executor = await services.GetRequestExecutorAsync();

        using var response = await ExecuteAsync(
            executor,
            "{ items { totalCount } }");

        Assert.Multiple(() =>
        {
            Assert.That(
                GetData(response, "items").GetProperty("totalCount").GetInt32(),
                Is.EqualTo(5));
            Assert.That(TrackedItem.ConstructionCount, Is.Zero);
            AssertCountOnlyCommand();
        });
    }

    [Test]
    public async Task AliasedTotalCountAndTypename_StayMetadataOnly()
    {
        using var response = await ExecuteAsync(
            """
            {
              page: items {
                count: totalCount
                __typename
              }
            }
            """);
        var page = GetData(response, "page");

        Assert.Multiple(() =>
        {
            Assert.That(page.GetProperty("count").GetInt32(), Is.EqualTo(5));
            Assert.That(page.GetProperty("__typename").GetString(), Is.EqualTo("ItemsCollectionSegment"));
            Assert.That(TrackedItem.ConstructionCount, Is.Zero);
            AssertCountOnlyCommand();
        });
    }

    [Test]
    public async Task AliasedCounters_ExecuteOnlyFourFilteredCounts()
    {
        using var response = await ExecuteAsync(
            """
            {
              allUsers: items { totalCount __typename }
              activeUsers: items(where: { id: { gt: 2 } }) { totalCount __typename }
              blockedUsers: items(where: { id: { lte: 2 } }) { totalCount __typename }
              newUsers: items(where: { id: { gte: 5 } }) { totalCount __typename }
            }
            """);
        var data = response.RootElement.GetProperty("data");

        Assert.Multiple(() =>
        {
            Assert.That(data.GetProperty("allUsers").GetProperty("totalCount").GetInt32(), Is.EqualTo(5));
            Assert.That(data.GetProperty("activeUsers").GetProperty("totalCount").GetInt32(), Is.EqualTo(3));
            Assert.That(data.GetProperty("blockedUsers").GetProperty("totalCount").GetInt32(), Is.EqualTo(2));
            Assert.That(data.GetProperty("newUsers").GetProperty("totalCount").GetInt32(), Is.EqualTo(1));
            Assert.That(TrackedItem.ConstructionCount, Is.Zero);
            Assert.That(_trace.Commands, Has.Count.EqualTo(4));
            Assert.That(
                _trace.Commands,
                Has.All.Contains("Count(*)").IgnoreCase);
        });
    }

    [Test]
    public async Task FragmentAndSkippedItems_UseEffectiveSelectionAndAvoidMaterialization()
    {
        using var response = await ExecuteAsync(
            """
            query ItemsPage($includeItems: Boolean!) {
              page: items(take: 2) {
                ...CountFields
                items @include(if: $includeItems) { id }
              }
            }

            fragment CountFields on ItemsCollectionSegment {
              count: totalCount
            }
            """,
            """{ "includeItems": false }""");

        Assert.Multiple(() =>
        {
            Assert.That(GetData(response, "page").GetProperty("count").GetInt32(), Is.EqualTo(5));
            Assert.That(TrackedItem.ConstructionCount, Is.Zero);
            AssertCountOnlyCommand();
        });
    }

    [Test]
    public async Task FragmentAndIncludedItems_MaterializeExactRequestedPageWhenCountIsAvailable()
    {
        using var response = await ExecuteAsync(
            """
            query ItemsPage($includeItems: Boolean!) {
              page: items(take: 2) {
                ...CountFields
                items @include(if: $includeItems) { id }
              }
            }

            fragment CountFields on ItemsCollectionSegment {
              count: totalCount
            }
            """,
            """{ "includeItems": true }""");
        var page = GetData(response, "page");

        Assert.Multiple(() =>
        {
            Assert.That(ReadIds(page), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(page.GetProperty("count").GetInt32(), Is.EqualTo(5));
            Assert.That(TrackedItem.ConstructionCount, Is.EqualTo(2));
            Assert.That(_trace.Commands, Has.Count.EqualTo(2));
            AssertItemCommand(take: 2);
            AssertCountCommand();
        });
    }

    [Test]
    public async Task ItemsOnly_UsesExactTakeWithoutLookaheadOrCount()
    {
        using var response = await ExecuteAsync("{ items(take: 2) { items { id name } } }");

        Assert.Multiple(() =>
        {
            Assert.That(ReadIds(GetData(response, "items")), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(TrackedItem.ConstructionCount, Is.EqualTo(2));
            Assert.That(_trace.Commands, Has.Count.EqualTo(1));
            AssertItemCommand(take: 2);
            AssertNoCountCommand();
        });
    }

    [Test]
    public async Task ItemsAndHasPrevious_UseExactTakeAndComputePreviousFromSkip()
    {
        using var response = await ExecuteAsync(
            """
            {
              items(skip: 1, take: 2) {
                items { id }
                pageInfo { hasPreviousPage }
              }
            }
            """);
        var page = GetData(response, "items");

        Assert.Multiple(() =>
        {
            Assert.That(ReadIds(page), Is.EqualTo(new[] { 2, 3 }));
            Assert.That(page.GetProperty("pageInfo").GetProperty("hasPreviousPage").GetBoolean(), Is.True);
            Assert.That(TrackedItem.ConstructionCount, Is.EqualTo(2));
            Assert.That(_trace.Commands, Has.Count.EqualTo(1));
            AssertItemCommand(take: 2);
        });
    }

    [Test]
    public async Task ItemsAndHasNextWithoutCount_UseOneLookaheadAndTrimItFromResponse()
    {
        using var response = await ExecuteAsync(
            """
            {
              items(take: 2) {
                items { id }
                pageInfo { hasNextPage }
              }
            }
            """);
        var page = GetData(response, "items");

        Assert.Multiple(() =>
        {
            Assert.That(ReadIds(page), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(page.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean(), Is.True);
            Assert.That(TrackedItem.ConstructionCount, Is.EqualTo(3));
            Assert.That(_trace.Commands, Has.Count.EqualTo(1));
            AssertItemCommand(take: 3);
            AssertNoCountCommand();
        });
    }

    [Test]
    public async Task ItemsCountAndHasNext_UseExactTakeAndDeriveHasNextFromCount()
    {
        using var response = await ExecuteAsync(
            """
            {
              items(take: 2) {
                items { id }
                totalCount
                pageInfo { hasNextPage }
              }
            }
            """);
        var page = GetData(response, "items");

        Assert.Multiple(() =>
        {
            Assert.That(ReadIds(page), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(page.GetProperty("totalCount").GetInt32(), Is.EqualTo(5));
            Assert.That(page.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean(), Is.True);
            Assert.That(TrackedItem.ConstructionCount, Is.EqualTo(2));
            Assert.That(_trace.Commands, Has.Count.EqualTo(2));
            AssertItemCommand(take: 2);
            AssertCountCommand();
        });
    }

    [Test]
    public async Task CountAndPageInfoWithoutItems_UseCountOnlyAndDeriveBothFlags()
    {
        using var response = await ExecuteAsync(
            """
            {
              items(skip: 2, take: 2) {
                totalCount
                pageInfo { hasNextPage hasPreviousPage }
              }
            }
            """);
        var page = GetData(response, "items");
        var pageInfo = page.GetProperty("pageInfo");

        Assert.Multiple(() =>
        {
            Assert.That(page.GetProperty("totalCount").GetInt32(), Is.EqualTo(5));
            Assert.That(pageInfo.GetProperty("hasNextPage").GetBoolean(), Is.True);
            Assert.That(pageInfo.GetProperty("hasPreviousPage").GetBoolean(), Is.True);
            Assert.That(TrackedItem.ConstructionCount, Is.Zero);
            AssertCountOnlyCommand();
        });
    }

    [TestCase(0, 2, true)]
    [TestCase(3, 2, false)]
    public async Task HasNextWithoutItems_UsesExistenceQueryWithoutMaterializingPage(
        int skip,
        int take,
        bool expectedHasNext)
    {
        using var response = await ExecuteAsync(
            $$"""
            {
              items(skip: {{skip}}, take: {{take}}) {
                pageInfo { hasNextPage }
              }
            }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(
                GetData(response, "items").GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean(),
                Is.EqualTo(expectedHasNext));
            Assert.That(TrackedItem.ConstructionCount, Is.Zero);
            Assert.That(_trace.Commands, Has.Count.EqualTo(1));
            AssertNoCountCommand();
            AssertExistenceCommand(expectedOffset: skip + take);
        });
    }

    [Test]
    public async Task HasPreviousWithoutItems_DoesNotAccessSource()
    {
        using var response = await ExecuteAsync(
            "{ items(skip: 2, take: 2) { pageInfo { hasPreviousPage } } }");

        Assert.Multiple(() =>
        {
            Assert.That(
                GetData(response, "items").GetProperty("pageInfo").GetProperty("hasPreviousPage").GetBoolean(),
                Is.True);
            Assert.That(TrackedItem.ConstructionCount, Is.Zero);
            Assert.That(_trace.Commands, Is.Empty);
        });
    }

    [Test]
    public async Task TypenameOnly_DoesNotAccessSource()
    {
        using var response = await ExecuteAsync("{ items { __typename } }");

        Assert.Multiple(() =>
        {
            Assert.That(GetData(response, "items").GetProperty("__typename").GetString(),
                Is.EqualTo("ItemsCollectionSegment"));
            Assert.That(TrackedItem.ConstructionCount, Is.Zero);
            Assert.That(_trace.Commands, Is.Empty);
        });
    }

    [Test]
    public async Task PagingObserver_ReceivesItemsAndPageInfoForMetadataOnlySelection()
    {
        var observer = new CapturingPageObserver();
        await using var services = CreateServices(
            useSelectionAwareProvider: true,
            observer);
        var executor = await services.GetRequestExecutorAsync();

        TrackedItem.ResetConstructionCount();
        _trace.Clear();

        var result = await executor.ExecuteAsync(
            "{ observedItems(take: 2) { __typename } }");
        using var response = JsonDocument.Parse(result.ToJson());

        Assert.Multiple(() =>
        {
            Assert.That(response.RootElement.TryGetProperty("errors", out _), Is.False);
            Assert.That(observer.ItemIds, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(observer.HasNextPage, Is.True);
            Assert.That(observer.HasPreviousPage, Is.False);
            Assert.That(TrackedItem.ConstructionCount, Is.EqualTo(3));
            Assert.That(_trace.Commands, Has.Count.EqualTo(1));
            AssertItemCommand(take: 3);
            AssertNoCountCommand();
        });
    }

    [Test]
    public async Task CustomSegmentFieldWithoutItems_MaterializesExactPageForResolverCompatibility()
    {
        using var response = await ExecuteAsync(
            "{ items(skip: 1, take: 2) { sumOfIdsOnPage } }");

        Assert.Multiple(() =>
        {
            Assert.That(GetData(response, "items").GetProperty("sumOfIdsOnPage").GetInt32(), Is.EqualTo(5));
            Assert.That(TrackedItem.ConstructionCount, Is.EqualTo(2));
            Assert.That(_trace.Commands, Has.Count.EqualTo(1));
            AssertItemCommand(take: 2);
            AssertNoCountCommand();
        });
    }

    [Test]
    public async Task FilteredTotalCount_CountsFilteredSourceWithoutMaterialization()
    {
        using var response = await ExecuteAsync(
            "{ items(where: { id: { gt: 2 } }) { totalCount } }");

        Assert.Multiple(() =>
        {
            Assert.That(GetData(response, "items").GetProperty("totalCount").GetInt32(), Is.EqualTo(3));
            Assert.That(TrackedItem.ConstructionCount, Is.Zero);
            AssertCountOnlyCommand();
            Assert.That(_trace.Commands.Single(), Does.Contain("WHERE").IgnoreCase);
        });
    }

    [Test]
    public async Task SortingAndItemsOnly_PreserveOrderAndExactTake()
    {
        using var response = await ExecuteAsync(
            "{ sortableItems(take: 2, order: [{ id: DESC }]) { items { id } } }");

        Assert.Multiple(() =>
        {
            Assert.That(ReadIds(GetData(response, "sortableItems")), Is.EqualTo(new[] { 5, 4 }));
            Assert.That(TrackedItem.ConstructionCount, Is.EqualTo(2));
            Assert.That(_trace.Commands, Has.Count.EqualTo(1));
            AssertItemCommand(take: 2);
            Assert.That(_trace.Commands.Single(), Does.Contain("DESC").IgnoreCase);
        });
    }

    [TestCase(4, new[] { 5 }, false)]
    [TestCase(5, new int[] { }, false)]
    public async Task LastAndBeyondLastPage_ReturnCorrectItemsCountAndFlags(
        int skip,
        int[] expectedIds,
        bool expectedHasNext)
    {
        using var response = await ExecuteAsync(
            $$"""
            {
              items(skip: {{skip}}, take: 2) {
                items { id }
                totalCount
                pageInfo { hasNextPage hasPreviousPage }
              }
            }
            """);
        var page = GetData(response, "items");
        var pageInfo = page.GetProperty("pageInfo");

        Assert.Multiple(() =>
        {
            Assert.That(ReadIds(page), Is.EqualTo(expectedIds));
            Assert.That(page.GetProperty("totalCount").GetInt32(), Is.EqualTo(5));
            Assert.That(pageInfo.GetProperty("hasNextPage").GetBoolean(), Is.EqualTo(expectedHasNext));
            Assert.That(pageInfo.GetProperty("hasPreviousPage").GetBoolean(), Is.True);
            Assert.That(TrackedItem.ConstructionCount, Is.EqualTo(expectedIds.Length));
            Assert.That(_trace.Commands, Has.Count.EqualTo(2));
            AssertItemCommand(take: 2);
            AssertCountCommand();
        });
    }

    [Test]
    public async Task TakeZero_ReturnsEmptyItemsAndUsesExistenceWithoutMaterialization()
    {
        using var response = await ExecuteAsync(
            "{ items(take: 0) { items { id } pageInfo { hasNextPage } } }");
        var page = GetData(response, "items");

        Assert.Multiple(() =>
        {
            Assert.That(ReadIds(page), Is.Empty);
            Assert.That(page.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean(), Is.True);
            Assert.That(TrackedItem.ConstructionCount, Is.Zero);
            Assert.That(_trace.Commands, Has.Count.EqualTo(1));
            AssertNoCountCommand();
            AssertExistenceCommand(expectedOffset: 0);
        });
    }

    [Test]
    public async Task DefaultPageSize_IsAppliedWithoutUnrequestedLookahead()
    {
        using var response = await ExecuteAsync("{ items { items { id } } }");

        Assert.Multiple(() =>
        {
            Assert.That(ReadIds(GetData(response, "items")), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(TrackedItem.ConstructionCount, Is.EqualTo(2));
            Assert.That(_trace.Commands, Has.Count.EqualTo(1));
            AssertItemCommand(take: 2);
        });
    }

    [Test]
    public async Task NullTake_TotalCountOnly_ExecutesOnlyCountWithoutMaterialization()
    {
        using var response = await ExecuteAsync("{ unboundedItems { totalCount } }");

        Assert.Multiple(() =>
        {
            Assert.That(
                GetData(response, "unboundedItems").GetProperty("totalCount").GetInt32(),
                Is.EqualTo(5));
            Assert.That(TrackedItem.ConstructionCount, Is.Zero);
            AssertCountOnlyCommand();
        });
    }

    [Test]
    public async Task NullTake_ItemsAndMetadata_PreserveUnboundedUpstreamSemantics()
    {
        using var response = await ExecuteAsync(
            """
            {
              unboundedItems(skip: 2) {
                items { id }
                totalCount
                pageInfo { hasNextPage hasPreviousPage }
              }
            }
            """);
        var page = GetData(response, "unboundedItems");
        var pageInfo = page.GetProperty("pageInfo");
        var sql = string.Join(Environment.NewLine, _trace.Commands);

        Assert.Multiple(() =>
        {
            Assert.That(ReadIds(page), Is.EqualTo(new[] { 3, 4, 5 }));
            Assert.That(page.GetProperty("totalCount").GetInt32(), Is.EqualTo(5));
            Assert.That(pageInfo.GetProperty("hasNextPage").GetBoolean(), Is.False);
            Assert.That(pageInfo.GetProperty("hasPreviousPage").GetBoolean(), Is.True);
            Assert.That(TrackedItem.ConstructionCount, Is.EqualTo(3));
            Assert.That(_trace.Commands, Has.Count.EqualTo(2));
            Assert.That(sql, Does.Match(@"(?is)@skip\s*=\s*2\b"));
            Assert.That(sql, Does.Not.Match(@"(?is)@take\b"));
            AssertCountCommand();
        });
    }

    [Test]
    public async Task InMemoryEnumerable_UsesSameSelectionAwareSemantics()
    {
        using var response = await ExecuteAsync(
            """
            {
              memoryItems(take: 2) {
                items { id }
                totalCount
                pageInfo { hasNextPage }
              }
            }
            """);
        var page = GetData(response, "memoryItems");

        Assert.Multiple(() =>
        {
            Assert.That(ReadIds(page), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(page.GetProperty("totalCount").GetInt32(), Is.EqualTo(5));
            Assert.That(page.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean(), Is.True);
            Assert.That(_trace.Commands, Is.Empty);
        });
    }

    [Test]
    public async Task ExecutableSource_UnwrapsToQueryableAndPreservesSemantics()
    {
        using var response = await ExecuteAsync(
            """
            {
              executableItems(take: 2) {
                items { id }
                totalCount
                pageInfo { hasNextPage }
              }
            }
            """);
        var page = GetData(response, "executableItems");

        Assert.Multiple(() =>
        {
            Assert.That(ReadIds(page), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(page.GetProperty("totalCount").GetInt32(), Is.EqualTo(5));
            Assert.That(page.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean(), Is.True);
            Assert.That(_trace.Commands, Is.Empty);
        });
    }

    [Test]
    public async Task SelectionAwareProvider_DoesNotChangeGraphQlSchema()
    {
        await using var baselineServices = CreateServices(useSelectionAwareProvider: false);
        var baselineExecutor = await baselineServices.GetRequestExecutorAsync();

        Assert.That(_executor.Schema.ToString(), Is.EqualTo(baselineExecutor.Schema.ToString()));
    }

    private ServiceProvider CreateServices(
        bool useSelectionAwareProvider,
        CapturingPageObserver? observer = null,
        Action<IRequestExecutorBuilder>? configureProviders = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_trace);
        services.AddScoped(_ => PaginationTestDatabase.CreateConnection(_dataOptions, _trace));

        var graphQl = services
            .AddGraphQLServer()
            .AddQueryType<PaginationQuery>()
            .AddType<ItemsCollectionSegmentExtensions>()
            .AddFiltering()
            .AddSorting()
            .ModifyRequestOptions(options => options.IncludeExceptionDetails = true)
            .ModifyPagingOptions(options =>
            {
                options.DefaultPageSize = 2;
                options.MaxPageSize = 100;
                options.IncludeTotalCount = true;
            });

        if (useSelectionAwareProvider)
        {
            graphQl.AddSelectionAwareOffsetPaging(defaultProvider: true);
        }

        configureProviders?.Invoke(graphQl);

        if (observer is not null)
        {
            graphQl.UseField(next => async context =>
            {
                if (context.Selection.Field.Name == "observedItems")
                {
                    context.RegisterPageObserver(observer);
                }

                await next(context);
            });
        }

        return services.BuildServiceProvider();
    }

    private Task<JsonDocument> ExecuteAsync(string document, string? variablesJson = null) =>
        ExecuteAsync(_executor, document, variablesJson);

    private async Task<JsonDocument> ExecuteAsync(
        IRequestExecutor executor,
        string document,
        string? variablesJson = null)
    {
        TrackedItem.ResetConstructionCount();
        _trace.Clear();

        var requestBuilder = OperationRequestBuilder.New().SetDocument(document);
        if (variablesJson is not null)
        {
            requestBuilder.SetVariableValues(variablesJson);
        }

        using var request = requestBuilder.Build();
        var result = await executor.ExecuteAsync(request);
        var response = JsonDocument.Parse(result.ToJson());

        if (response.RootElement.TryGetProperty("errors", out var errors))
        {
            var errorJson = errors.GetRawText();
            response.Dispose();
            Assert.Fail($"GraphQL execution failed: {errorJson}");
        }

        return response;
    }

    private static JsonElement GetData(JsonDocument response, string fieldName) =>
        response.RootElement.GetProperty("data").GetProperty(fieldName);

    private static int[] ReadIds(JsonElement page) =>
        page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .ToArray();

    private void AssertCountOnlyCommand()
    {
        Assert.That(_trace.Commands, Has.Count.EqualTo(1));
        AssertCountCommand();
    }

    private void AssertCountCommand() =>
        Assert.That(
            string.Join(Environment.NewLine, _trace.Commands),
            Does.Contain("Count(*)").IgnoreCase);

    private void AssertNoCountCommand() =>
        Assert.That(
            string.Join(Environment.NewLine, _trace.Commands),
            Does.Not.Contain("Count(*)").IgnoreCase);

    private void AssertItemCommand(int take)
    {
        var sql = string.Join(Environment.NewLine, _trace.Commands);
        Assert.That(sql, Does.Match($@"(?is)@take\s*=\s*{take}\b"));
    }

    private void AssertExistenceCommand(int expectedOffset)
    {
        var sql = string.Join(Environment.NewLine, _trace.Commands);

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Match($@"(?is)@skip\s*=\s*{expectedOffset}\b"));
            Assert.That(sql, Does.Match(@"(?is)\bEXISTS\s*\("));
        });
    }
}
