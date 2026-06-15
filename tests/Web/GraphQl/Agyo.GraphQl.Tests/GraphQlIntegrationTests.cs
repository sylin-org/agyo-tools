using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Agyo.Testing.Integration;
using Agyo.Web.GraphQl;
using Agyo.Web.GraphQl.Controllers;
using Agyo.Web.GraphQl.Execution;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.GraphQl.Tests;

/// <summary>
/// A tiny <c>IEntity&lt;string&gt;</c> (via <see cref="Entity{TSelf}"/>) used only by these specs.
/// It is auto-discovered by <c>AddKoanGraphQl()</c> (which scans loaded assemblies for
/// <c>IEntity&lt;&gt;</c> implementers) and projected into the schema as the <c>graphQlWidget</c>
/// type plus the <c>graphQlWidgets</c> collection query field. Its storage name under the InMemory
/// adapter resolves to the bare CLR name (<c>GraphQlWidget</c>), so the GraphQL field names are
/// deterministic.
/// </summary>
public sealed class GraphQlWidget : Entity<GraphQlWidget>
{
    public string? Name { get; set; }
}

/// <summary>
/// One real Koan web host (TestServer) shared by the HTTP-endpoint specs in
/// <see cref="GraphQlIntegrationTests"/>.
/// </summary>
/// <remarks>
/// The host is built through real <c>AddKoan()</c> reflective discovery, so the GraphQl
/// <c>KoanModule</c> is found and wired by the framework — genuine ARCH-0079 composition, not
/// hand-registration. A shared <see cref="TestServer"/> keeps the HTTP specs cheap; multi-container
/// safety is proven independently by <see cref="GraphQlMultiContainerTests"/>, which stands up two
/// separate containers in this same process.
/// </remarks>
public sealed class GraphQlHostFixture : IAsyncLifetime
{
    private IHost? _host;

    public TestServer Server => _host!.GetTestServer();
    public IServiceProvider Services => _host!.Services;

    public async Task InitializeAsync()
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer()
               .UseEnvironment("Test")
               .ConfigureServices(s =>
               {
                   // Real reflective discovery: AddKoan() finds the InMemory adapter + the GraphQl
                   // KoanModule, which itself calls AddKoanGraphQl(). The explicit AddKoanGraphQl()
                   // call is belt-and-suspenders for the controller wiring and is idempotent
                   // per IServiceCollection.
                   s.AddKoan();
                   s.AddKoanGraphQl();
                   s.AddControllers().AddApplicationPart(typeof(GraphQlController).Assembly);
               })
               .Configure(app =>
               {
                   app.UseRouting();
                   app.UseEndpoints(e => e.MapControllers());
               });
        });

        _host = await builder.StartAsync();

        // AppHost.Current backs the GraphQL extension's storage-name resolution; the real Koan web
        // bootstrap sets it during boot, so set it here to mirror a production host exactly.
        AppHost.Current = _host.Services;
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}

/// <summary>
/// ARCH-0079 integration specs for the <c>Agyo.Web.GraphQl</c> capability, exercised over a real
/// <c>AddKoan()</c>-discovered web host. The boot-smoke spec proves the <c>KoanGraphQlModule</c> is
/// found by reflective discovery and stands up its primary surface (<see cref="IGraphQlExecutor"/> +
/// the HotChocolate schema); the behavioral specs prove an entity round-trips over the HTTP endpoint,
/// including the <c>totalCount</c> field whose <c>long</c> count is projected to the GraphQL Int.
/// </summary>
public sealed class GraphQlIntegrationTests : IClassFixture<GraphQlHostFixture>
{
    private readonly GraphQlHostFixture _fixture;
    private readonly ITestOutputHelper _output;

    public GraphQlIntegrationTests(GraphQlHostFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// BOOT-SMOKE: the real <see cref="IHost"/> built via <c>AddKoan()</c> must — through reflective
    /// discovery of <c>KoanGraphQlModule</c> — register the capability's primary surface
    /// (<see cref="IGraphQlExecutor"/>) and stand up a valid HotChocolate schema. If the module is not
    /// discovered, the resolve throws and this fails, which would be a genuine migration finding.
    /// </summary>
    [Fact]
    public async Task Boot_resolves_graphql_executor_and_schema_via_reflective_discovery()
    {
        var executor = _fixture.Services.GetRequiredService<IGraphQlExecutor>();
        executor.Should().NotBeNull("the discovered KoanGraphQlModule registers IGraphQlExecutor via AddKoanGraphQl()");

        var sdl = await executor.GetSdl(CancellationToken.None);
        _output.WriteLine(sdl);

        sdl.Should().NotBeNullOrWhiteSpace("the HotChocolate request executor must produce a valid schema");
        sdl.Should().Contain("type Query", "the auto-built schema always exposes a Query root type");
        sdl.Should().Contain("entities", "AddKoanGraphQl always exposes the 'entities' discovery field so the schema is valid");
    }

    /// <summary>
    /// BEHAVIORAL: seed one <see cref="GraphQlWidget"/> row through the InMemory adapter, then POST a
    /// GraphQL collection query to <c>/graphql</c> via <see cref="TestServer.CreateClient"/>. The
    /// endpoint must return HTTP 200 and the seeded entity (id + name) inside
    /// <c>data.graphQlWidgets.items</c>.
    /// </summary>
    [Fact]
    public async Task Post_collection_query_returns_seeded_entity_over_graphql_endpoint()
    {
        // Unique marker so the assertion is robust against the process-global InMemory store.
        var marker = "widget-" + Guid.NewGuid().ToString("N");

        var widget = new GraphQlWidget { Name = marker };
        var saved = await widget.Save();
        saved.Id.Should().NotBeNullOrWhiteSpace("Entity<T>.Save() assigns a GUID v7 id");

        var client = _fixture.Server.CreateClient();
        var query = "{ graphQlWidgets { items { id name } } }";
        var response = await client.PostAsJsonAsync("/graphql", new { query });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        _output.WriteLine(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("errors", out _).Should().BeFalse("the items-only query must execute without GraphQL errors");

        var items = root.GetProperty("data").GetProperty("graphQlWidgets").GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0, "the seeded widget must be returned by the collection query");

        var found = false;
        foreach (var item in items.EnumerateArray())
        {
            if (item.GetProperty("name").GetString() == marker)
            {
                item.GetProperty("id").GetString().Should().Be(saved.Id,
                    "the entity returned over GraphQL must carry the persisted id");
                found = true;
                break;
            }
        }

        found.Should().BeTrue($"the seeded entity '{marker}' must appear in data.graphQlWidgets.items");
    }

    /// <summary>
    /// BEHAVIORAL (totalCount): seed one <see cref="GraphQlWidget"/> row, then POST a collection query
    /// that requests <c>totalCount</c> alongside <c>items</c>. The collection payload's
    /// <c>TotalCount</c> is a <see cref="long"/>; the GraphQL Int resolver must project it without an
    /// <see cref="InvalidCastException"/> (the boxed-long unbox bug). The query must execute without
    /// GraphQL errors and report a count that covers the seeded rows.
    /// </summary>
    [Fact]
    public async Task Post_collection_query_with_totalCount_returns_count_without_cast_error()
    {
        var marker = "widget-" + Guid.NewGuid().ToString("N");

        var widget = new GraphQlWidget { Name = marker };
        var saved = await widget.Save();
        saved.Id.Should().NotBeNullOrWhiteSpace("Entity<T>.Save() assigns a GUID v7 id");

        var client = _fixture.Server.CreateClient();
        var query = "{ graphQlWidgets { totalCount items { id name } } }";
        var response = await client.PostAsJsonAsync("/graphql", new { query });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        _output.WriteLine(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Before the fix, the long->int unbox threw InvalidCastException and surfaced here as a
        // GraphQL error on the totalCount field; after the fix the query is error-free.
        root.TryGetProperty("errors", out _).Should().BeFalse(
            "requesting totalCount must not throw InvalidCastException projecting the long count to the GraphQL Int");

        var collection = root.GetProperty("data").GetProperty("graphQlWidgets");

        var items = collection.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0, "the seeded widget must be returned");

        var totalCount = collection.GetProperty("totalCount");
        totalCount.ValueKind.Should().Be(JsonValueKind.Number, "totalCount projects the long count to a GraphQL Int");
        totalCount.GetInt32().Should().BeGreaterThanOrEqualTo(items.GetArrayLength(),
            "the reported total count must cover at least the rows returned in this page");
        totalCount.GetInt32().Should().BeGreaterThan(0, "at least the seeded widget is counted");
    }
}

/// <summary>
/// MULTI-CONTAINER SAFETY: <c>AddKoanGraphQl()</c> must register independently into every
/// <see cref="IServiceCollection"/> in the same process. The capability previously latched a
/// <c>private static bool</c> guard, so the second container in a process silently skipped
/// registration and never wired <see cref="IGraphQlExecutor"/>. These specs stand up two fully
/// independent <see cref="AgyoIntegrationHost"/> containers (each its own <c>AddKoan()</c> +
/// <c>AddKoanGraphQl()</c>) and assert GraphQl wires in BOTH — they would fail with the process-global
/// static.
/// </summary>
public sealed class GraphQlMultiContainerTests
{
    private readonly ITestOutputHelper _output;

    public GraphQlMultiContainerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static Task<IntegrationHost> BuildHostAsync() =>
        AgyoIntegrationHost.Configure()
            .ConfigureServices(services =>
            {
                services.AddKoan();
                services.AddKoanGraphQl();
            })
            .StartAsync();

    /// <summary>
    /// Two independent containers in one process must BOTH resolve <see cref="IGraphQlExecutor"/> and
    /// produce a valid schema. With the former process-global static, the second container's
    /// <c>AddKoanGraphQl()</c> would no-op and <c>GetRequiredService&lt;IGraphQlExecutor&gt;()</c> would
    /// throw — the exact failure this spec guards against.
    /// </summary>
    [Fact]
    public async Task Two_independent_containers_each_wire_graphql_executor_and_schema()
    {
        await using var first = await BuildHostAsync();
        await using var second = await BuildHostAsync();

        first.Services.Should().NotBeSameAs(second.Services, "each host owns an independent DI container");

        foreach (var (host, label) in new[] { (first, "first"), (second, "second") })
        {
            var executor = host.Services.GetRequiredService<IGraphQlExecutor>();
            executor.Should().NotBeNull(
                $"AddKoanGraphQl must register IGraphQlExecutor into the {label} container independently");

            var sdl = await executor.GetSdl(CancellationToken.None);
            _output.WriteLine($"[{label}] {sdl}");
            sdl.Should().NotBeNullOrWhiteSpace($"the {label} container must stand up a valid HotChocolate schema");
            sdl.Should().Contain("type Query", $"the {label} container's schema exposes a Query root type");
            sdl.Should().Contain("entities", $"the {label} container's schema exposes the discovery field");
        }
    }

    /// <summary>
    /// Calling <c>AddKoanGraphQl()</c> twice on the SAME <see cref="IServiceCollection"/> must be
    /// idempotent: the marker-based guard registers exactly once, so the container still resolves a
    /// single working executor (no duplicate-registration faults).
    /// </summary>
    [Fact]
    public async Task Repeated_registration_on_same_collection_is_idempotent()
    {
        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(services =>
            {
                services.AddKoan();
                services.AddKoanGraphQl();
                services.AddKoanGraphQl();
                services.AddKoanGraphQl();
            })
            .StartAsync();

        var executor = host.Services.GetRequiredService<IGraphQlExecutor>();
        var sdl = await executor.GetSdl(CancellationToken.None);
        sdl.Should().Contain("type Query", "repeated registration on one collection stays a single valid schema");
    }
}
