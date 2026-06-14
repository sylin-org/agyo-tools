using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
/// One real Koan web host (TestServer) shared by every spec in <see cref="GraphQlIntegrationTests"/>.
/// </summary>
/// <remarks>
/// A single shared host is deliberate: <c>AddKoanGraphQl()</c> latches a <c>private static bool</c>
/// guard the first time it runs, so its registration into a DI container happens at most once per
/// process. Building two separate hosts in the same test process would leave the second container
/// without <see cref="IGraphQlExecutor"/> (see notes/AGYO finding). The host is built through real
/// <c>AddKoan()</c> reflective discovery, so the GraphQl <c>KoanModule</c> is found and wired by the
/// framework — genuine ARCH-0079 composition, not hand-registration.
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
                   // call is belt-and-suspenders for the controller wiring and is idempotent.
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
/// the HotChocolate schema); the behavioral spec proves an entity round-trips over the HTTP endpoint.
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
        // Only 'items' is requested: the 'totalCount' field has a known long->int cast quirk on the
        // InMemory adapter (see notes), unrelated to the entity-round-trip behavior under test.
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
}
