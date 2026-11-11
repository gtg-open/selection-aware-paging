using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Text.Json;
using HotChocolate.AspNetCore;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Is = NUnit.Framework.Is;

namespace GtG.Open.HC.SelectionAwarePaging.Tests;

[TestFixture]
[NonParallelizable]
public sealed class SelectionAwareOffsetPagingHttpTests
{
    [Test]
    public async Task CountOnlyRequest_ThroughRealHttp_ReturnsWireCountAndExecutesOnlyCountSql()
    {
        var trace = new SqlTrace();
        await using var database = await PaginationTestDatabase.CreateSeededAsync(trace);

        var host = CreateHost(database.DataOptions, trace);
        var started = false;
        int? listenerPort = null;

        try
        {
            await host.StartAsync();
            started = true;

            var address = GetHostAddress(host);
            listenerPort = address.Port;
            AssertLoopbackOnlyListener(address);

            TrackedItem.ResetConstructionCount();
            trace.Clear();

            using var client = new HttpClient { BaseAddress = address };
            using var httpResponse = await client.PostAsJsonAsync(
                "/graphql",
                new { query = "{ items { totalCount } }" });
            var wireResponse = await httpResponse.Content.ReadAsStringAsync();

            Assert.That(httpResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), wireResponse);

            using var json = JsonDocument.Parse(wireResponse);
            Assert.Multiple(() =>
            {
                Assert.That(json.RootElement.TryGetProperty("errors", out _), Is.False, wireResponse);
                Assert.That(
                    json.RootElement.GetProperty("data")
                        .GetProperty("items")
                        .GetProperty("totalCount")
                        .GetInt32(),
                    Is.EqualTo(5));
                Assert.That(TrackedItem.ConstructionCount, Is.Zero);
                Assert.That(trace.Commands, Has.Count.EqualTo(1));
                Assert.That(trace.Commands.Single(), Does.Contain("Count(*)").IgnoreCase);
            });
        }
        finally
        {
            try
            {
                if (started)
                {
                    await host.StopAsync();
                }
            }
            finally
            {
                host.Dispose();

                if (listenerPort is { } port)
                {
                    await AssertListenerStopped(port);
                }
            }
        }
    }

    private static IHost CreateHost(DataOptions dataOptions, SqlTrace trace) =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls("http://127.0.0.1:0");
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton(trace);
                    services.AddScoped(_ =>
                        PaginationTestDatabase.CreateConnection(dataOptions, trace));
                    services
                        .AddGraphQLServer()
                        .AddSelectionAwareOffsetPaging(defaultProvider: true)
                        .AddQueryType<PaginationQuery>()
                        .AddType<ItemsCollectionSegmentExtensions>()
                        .AddFiltering()
                        .AddSorting()
                        .ModifyPagingOptions(options =>
                        {
                            options.DefaultPageSize = int.MaxValue - 1;
                            options.MaxPageSize = int.MaxValue - 1;
                            options.IncludeTotalCount = true;
                        });
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGraphQL());
                });
            })
            .Build();

    private static Uri GetHostAddress(IHost host)
    {
        var addresses = host.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()
            ?.Addresses;
        var address = addresses?.Single()
            ?? throw new InvalidOperationException("Kestrel did not expose exactly one address.");

        return new Uri(address);
    }

    private static void AssertLoopbackOnlyListener(Uri address)
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Where(endpoint => endpoint.Port == address.Port)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(address.Scheme, Is.EqualTo(Uri.UriSchemeHttp));
            Assert.That(address.Host, Is.EqualTo(IPAddress.Loopback.ToString()));
            Assert.That(listeners, Has.Length.GreaterThanOrEqualTo(1));
            Assert.That(
                listeners.All(endpoint => IPAddress.IsLoopback(endpoint.Address)),
                Is.True,
                $"Port {address.Port} was also exposed through: {string.Join(", ", listeners.Select(x => x.Address))}");
        });
    }

    private static async Task AssertListenerStopped(int port)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var isListening = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(endpoint => endpoint.Port == port);
            if (!isListening)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        Assert.Fail($"Temporary Kestrel listener on port {port} remained active after shutdown.");
    }
}
