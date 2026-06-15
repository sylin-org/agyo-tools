using Agyo.Service.Librarian.Models;
using Agyo.Service.Librarian.Services;
using AwesomeAssertions;
using Koan.Core;
using Koan.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.Service.Librarian.Tests;

/// <summary>
/// ARCH-0079 boot-smoke for Agyo.Service.Librarian. Boots a REAL <c>AddKoan()</c> host through
/// <see cref="Agyo.Testing.Integration.AgyoIntegrationHost"/> (hosted services start; genuine
/// reflective discovery of the Librarian's <c>KoanAutoRegistrar</c>) and asserts the whole service
/// surface composes from a single <c>AddKoan()</c> — the search/indexing pipeline, the
/// <see cref="IndexProjectAsync"/> delegate the MCP + Projects controllers inject, the file-watch
/// hosted services, and the maintenance task re-homed onto <c>Agyo.Scheduling</c>. No external
/// AI/Vector backend is required to boot; the InMemory data adapter is enough (the seeder hosted
/// service exercises the static <c>Entity&lt;T&gt;</c> data path during start).
/// </summary>
[Collection("LibrarianAmbientHost")]
public sealed class LibrarianBootSmokeTests
{
    private readonly ITestOutputHelper _output;

    public LibrarianBootSmokeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task AddKoan_composes_the_Librarian_service_surface()
    {
        await using var scope = await LibrarianHostScope.StartAsync(
            Agyo.Testing.Integration.AgyoIntegrationHost.Configure()
                // Pin the default data provider so the static Entity<T> facades resolve to InMemory.
                .WithSetting("Koan:Data:DefaultProvider", "inmemory")
                // Disable container provisioning: the Librarian references Koan.Orchestration.Aspire +
                // the Weaviate connector, whose evaluator would otherwise spin a Weaviate container on
                // boot (Auto mode). A boot-smoke composes services; it must not start infra.
                .WithSetting("Koan:Orchestration:Global", "Disabled")
                .ConfigureServices(services => services.AddKoan()));

        var sp = scope.Services;

        // 1. Singleton domain services compose from the root provider (moved from Program.cs into the
        //    registrar so the whole service is "Reference = Intent").
        sp.GetService<ProjectResolver>().Should().NotBeNull();
        sp.GetService<IndexingCoordinator>().Should().NotBeNull();
        sp.GetService<IIndexingResumptionQueue>().Should().NotBeNull();
        sp.GetService<TagResolver>().Should().NotBeNull();

        // 2. Hosted services wired via reflective discovery (the start above already ran them).
        var hosted = sp.GetServices<IHostedService>().Select(h => h.GetType().Name).ToList();
        hosted.Should().Contain("FileMonitoringService");
        hosted.Should().Contain("RagIngestionWorker",
            "ingest is re-platformed onto Rag's durable worker; the bespoke VectorSyncWorker outbox is retired (AGYO-0003)");
        hosted.Should().Contain("IndexingResumptionWorker");
        hosted.Should().Contain("TagSeedInitializer");

        // AGYO-0003: search + indexing are served by the Rag re-platform.
        sp.GetService<RagIndexService>().Should().NotBeNull();

        // 3. The maintenance task that depended on the (removed) Koan.Scheduling now rides
        //    Agyo.Scheduling and is discovered as an IScheduledTask.
        sp.GetServices<Agyo.Scheduling.IScheduledTask>()
          .Select(t => t.GetType().Name)
          .Should().Contain("JobMaintenanceTask",
              "the dangling Koan.Scheduling dependency was re-homed onto Sylin.Agyo.Scheduling");

        // 4. The per-request scoped surface (search + the MCP indexing delegate) resolves inside a scope.
        using var requestScope = sp.CreateScope();
        var rsp = requestScope.ServiceProvider;
        rsp.GetService<ISearchService>().Should().NotBeNull(
            "ISearchService backs the get-references MCP tool and the /api/search REST surface");
        rsp.GetService<IndexProjectAsync>().Should().NotBeNull(
            "the IndexProjectAsync delegate is injected into McpToolsController and ProjectsController");
        rsp.GetService<Indexer>().Should().NotBeNull();
        rsp.GetService<Discovery>().Should().NotBeNull();
        rsp.GetService<Search>().Should().NotBeNull();

        _output.WriteLine($"Hosted services: {string.Join(", ", hosted)}");
    }

    [Fact]
    public async Task Project_is_exposed_as_a_readonly_MCP_entity()
    {
        await using var scope = await LibrarianHostScope.StartAsync(
            Agyo.Testing.Integration.AgyoIntegrationHost.Configure()
                .WithSetting("Koan:Data:DefaultProvider", "inmemory")
                .WithSetting("Koan:Orchestration:Global", "Disabled")
                .ConfigureServices(services => services.AddKoan()));

        // The real /mcp transport lists tools from the McpEntityRegistry. Project must be registered
        // (read-only) so list_projects / project_status / resolve-by-query are genuine MCP tools — not
        // just the REST get-references surface. (Koan.Mcp tools are entity operations only; the Context7
        // search/reindex action verbs stay REST — AGYO-0002 P4b.)
        var registry = scope.Services.GetService<McpEntityRegistry>();
        registry.Should().NotBeNull("referencing Sylin.Koan.Mcp + AddKoan() registers the MCP entity registry");

        var project = registry!.Registrations.SingleOrDefault(r => r.EntityType == typeof(Project));
        project.Should().NotBeNull("Project carries [McpEntity], so the transport must list its tools");
        project!.Tools.Should().NotBeEmpty("a registered MCP entity exposes at least its read operations");
        project.Tools.Should().NotContain(t => t.IsMutation,
            "Project is [McpEntity(AllowMutations=false)] — only read tools should be exposed");

        _output.WriteLine($"MCP 'project' tools: {string.Join(", ", project.Tools.Select(t => t.Name))}");
    }

    [Fact]
    public async Task Context7_verbs_are_exposed_as_custom_MCP_tools()
    {
        await using var scope = await LibrarianHostScope.StartAsync(
            Agyo.Testing.Integration.AgyoIntegrationHost.Configure()
                .WithSetting("Koan:Data:DefaultProvider", "inmemory")
                .WithSetting("Koan:Orchestration:Global", "Disabled")
                .ConfigureServices(services => services.AddKoan()));

        // The Context7 verbs are real Koan.Mcp custom [McpTool] tools (AGYO-0002 P4b) — the transport
        // lists them via the custom-tool registry, not just the REST get-references surface.
        var customTools = scope.Services.GetService<Koan.Mcp.CustomTools.McpCustomToolRegistry>();
        customTools.Should().NotBeNull("the expanded Koan.Mcp registers the custom-tool registry");

        var names = customTools!.Tools.Select(t => t.Name).ToList();
        names.Should().Contain(
            new[] { "list_projects", "resolve_library_id", "project_status", "reindex_project", "get_library_docs" },
            "the Context7-compatible verbs are exposed as custom MCP tools");

        customTools.TryGet("reindex_project", out var reindex).Should().BeTrue();
        reindex.IsMutation.Should().BeTrue("reindex_project mutates state");

        customTools.TryGet("get_library_docs", out var docs).Should().BeTrue();
        ((Newtonsoft.Json.Linq.JObject)docs.InputSchema["properties"]!)["libraryId"].Should().NotBeNull();

        _output.WriteLine($"Custom MCP tools: {string.Join(", ", names)}");
    }
}

/// <summary>
/// Non-parallel collection for specs that touch the process-wide ambient <c>AppHost.Current</c>
/// via the Librarian's static <c>Entity&lt;T&gt;</c> facades. Keeps ambient-host boots serialized.
/// </summary>
[CollectionDefinition("LibrarianAmbientHost", DisableParallelization = true)]
public sealed class LibrarianAmbientHostCollection;
