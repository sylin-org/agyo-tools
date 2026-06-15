using Agyo.Service.Librarian.Models;
using Agyo.Service.Librarian.Services;
using Agyo.Testing.Infrastructure;
using AwesomeAssertions;
using Koan.Core;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.Service.Librarian.Tests;

/// <summary>
/// ARCH-0079 behavioral spec: the real ingest pipeline that backs the get-references MCP tool.
/// Container-gated (skip-clean) — it runs only when the operator supplies a Weaviate endpoint
/// (<c>AGYO_WEAVIATE_ENDPOINT</c>) and an Ollama endpoint (<c>AGYO_OLLAMA_ENDPOINT</c>, with the
/// <c>all-minilm</c> embedding model pulled). Absent either, it reports skipped.
/// <para>
/// Exercises the full re-platformed path end to end (AGYO-0003): Discovery → Agyo.Rag ingest (live Ollama
/// embeddings + inline Weaviate writes) → ISearchService hybrid search, and ASSERTS the cited round-trip —
/// the search returns the real stored chunk text (not the hydration placeholder) with file provenance.
/// Because Rag writes vectors inline at ingest and the Weaviate connector now returns stored metadata on
/// search, the bespoke async-outbox persistence gap is closed; the retrieval half is asserted, not logged.
/// </para>
/// </summary>
[Collection("LibrarianAmbientHost")]
public sealed class LibrarianIndexSearchBehavioralTests
{
    private const string WeaviateEnv = "AGYO_WEAVIATE_ENDPOINT";
    private const string OllamaEnv = "AGYO_OLLAMA_ENDPOINT";

    private readonly ITestOutputHelper _output;

    public LibrarianIndexSearchBehavioralTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task Index_then_search_returns_cited_chunks()
    {
        Skip.IfNot(InfraProbe.Available(WeaviateEnv), InfraProbe.Unavailable(WeaviateEnv));
        Skip.IfNot(InfraProbe.Available(OllamaEnv), InfraProbe.Unavailable(OllamaEnv));

        var weaviate = InfraProbe.ConnectionString(WeaviateEnv)!;
        var ollama = InfraProbe.ConnectionString(OllamaEnv)!;

        // A tiny throwaway repo with one markdown file to index.
        var repoDir = Path.Combine(Path.GetTempPath(), "agyo-librarian-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repoDir);
        await File.WriteAllTextAsync(Path.Combine(repoDir, "README.md"),
            "# Widget API\n\nThe authentication middleware validates bearer tokens before routing requests.\n");

        try
        {
            await using var scope = await LibrarianHostScope.StartAsync(
                Agyo.Testing.Integration.AgyoIntegrationHost.Configure()
                    .WithSetting("Koan:Data:DefaultProvider", "inmemory")
                    // Use the operator-provided backends; never provision containers from a test.
                    .WithSetting("Koan:Orchestration:Global", "Never")
                    .WithSetting("Koan:Data:Weaviate:Endpoint", weaviate)
                    .WithSetting("Koan:Data:Weaviate:Dimension", "384")
                    // Register the Ollama AI source explicitly so the embedding pipeline has a source.
                    // AllowDiscoveryInNonDev lets the Ollama contributor run in the Test env (it is
                    // otherwise dev-only). The embedding model is all-minilm (384-dim).
                    .WithSetting("Koan:Ai:AllowDiscoveryInNonDev", "true")
                    .WithSetting("Koan:Ai:Ollama:Urls:0", ollama)
                    // Route the Embed category to the 384-dim model (Rag's pipeline uses the default embed model).
                    .WithSetting("Koan:Ai:Embed:Model", "all-minilm")
                    .ConfigureServices(services => services.AddKoan()));

            var project = Project.Create("sample", repoDir);
            await project.Save();

            using var requestScope = scope.Services.CreateScope();
            var rsp = requestScope.ServiceProvider;
            var index = rsp.GetRequiredService<IndexProjectAsync>();
            var search = rsp.GetRequiredService<ISearchService>();

            // VERIFIED: the full ingest pipeline runs against live Ollama + Weaviate. ChunksCreated > 0
            // with no errors proves Discovery → Extraction → Chunker → Embedding(Ollama) → Indexer, and
            // (as a side effect) that the per-project Weaviate vector class is provisioned.
            var result = await index(project.Id, true, CancellationToken.None, null);
            _output.WriteLine($"Indexed: files={result.FilesProcessed} chunks={result.ChunksCreated} vectors={result.VectorsSaved} errors={result.Errors.Count}");
            foreach (var e in result.Errors)
                _output.WriteLine($"  ERROR [{e.ErrorType}] {e.FilePath}: {e.ErrorMessage}");
            result.Errors.Should().BeEmpty("the ingest pipeline should run cleanly against live infra");
            result.FilesProcessed.Should().BeGreaterThan(0, "Discovery should find the markdown file");
            result.ChunksCreated.Should().BeGreaterThan(0, "the markdown file should produce at least one chunk");

            // ASSERTED (AGYO-0003): the retrieval half now rides Agyo.Rag's INLINE vector writes — a chunk is
            // searchable the moment Ingest returns (no async-outbox lag), and the Weaviate connector returns the
            // stored chunk text + provenance on search (Koan-side fix), so the cited result is the REAL chunk,
            // not the hydration placeholder. The short retry only absorbs Weaviate's index-visibility latency.
            SearchResult? hit = null;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                var ctx = SearchRequestContext.Create(
                    "authentication middleware", projectIds: new[] { project.Id }, channel: SearchChannel.Mcp);
                var sr = await search.SearchAsync(project.Id, ctx, CancellationToken.None);
                if (sr.Chunks.Count > 0) { hit = sr; break; }
                await Task.Delay(2000);
            }

            hit.Should().NotBeNull("index→search must round-trip — Rag writes vectors inline at ingest");
            _output.WriteLine($"Search round-trip: HIT — {hit!.Chunks[0].Text}");

            var topChunk = hit.Chunks[0];
            topChunk.Text.Should().NotStartWith("[Chunk ",
                "the Weaviate connector must return the stored chunk text, not the entity-hydration placeholder");
            topChunk.Text.Should().Contain("bearer tokens",
                "the cited chunk must carry the real source content indexed from README.md");
            hit.Sources.Files.Should().Contain(
                f => f.FilePath.EndsWith("README.md", StringComparison.OrdinalIgnoreCase),
                "the cited result must carry file provenance (cite-by-file)");
        }
        finally
        {
            try { Directory.Delete(repoDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
