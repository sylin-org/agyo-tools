using Agyo.Rag;
using Agyo.Rag.Abstractions;
using Agyo.Testing.Infrastructure;
using AwesomeAssertions;
using Koan.Core;
using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.Rag.Tests;

/// <summary>
/// Container-gated behavioral spec: a minimal ingest-then-query over a tiny corpus through the
/// real RAG pipelines (embedding + vector + chat). Gated on AI infra via
/// <c>Skip.IfNot(InfraProbe.Available("AGYO_OLLAMA_ENDPOINT"))</c> — skips cleanly when no live
/// Ollama endpoint is provided. A vector backend (Qdrant) is wired when <c>AGYO_QDRANT_URL</c> is
/// also set; without it the spec asserts the documented degraded contract (corpus reports empty)
/// rather than fabricating an answer.
/// <para>
/// Joins the non-parallel <c>RagAmbientHost</c> collection because it drives the static
/// <see cref="Rag"/> facade over the process-wide ambient <c>AppHost.Current</c>.
/// </para>
/// </summary>
[Collection("RagAmbientHost")]
public sealed class RagIngestQueryBehavioralTests
{
    // Env vars the operator sets to point the spec at live infrastructure.
    private const string OllamaEnv = "AGYO_OLLAMA_ENDPOINT";
    private const string QdrantEnv = "AGYO_QDRANT_URL";

    private readonly ITestOutputHelper _output;

    public RagIngestQueryBehavioralTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A tiny corpus entity. Its [Embedding] body is what the pipeline embeds + retrieves.
    /// Deliberately NOT [RagCorpus]-decorated: the registrar scans the whole assembly for
    /// [RagCorpus] types and wires lifecycle hooks for each, and that path currently throws at
    /// boot (see RagCorpusLifecycleHookRegressionTests). The convention path used here
    /// (Rag.Corpus&lt;Fact&gt;().Ingest/Ask) needs no hooks. [Embedding] is class-level and does
    /// NOT trigger that discovery — it only names the embeddable property for EntityAi.ExtractText.
    /// </summary>
    [Embedding(Properties = new[] { nameof(Body) })]
    private sealed class Fact : Entity<Fact>
    {
        public string Body { get; set; } = string.Empty;
    }

    [SkippableFact]
    public async Task Ingest_then_query_over_a_tiny_corpus()
    {
        Skip.IfNot(
            InfraProbe.Available(OllamaEnv),
            InfraProbe.Unavailable(OllamaEnv));

        var ollama = InfraProbe.ConnectionString(OllamaEnv)!;
        var qdrant = InfraProbe.ConnectionString(QdrantEnv);

        var builder = Agyo.Testing.Integration.AgyoIntegrationHost.Configure()
            .WithSetting("Koan:Data:DefaultProvider", "inmemory")
            // Wire the live Ollama endpoint (both casings Koan accepts in samples).
            .WithSetting("Koan:Ai:Ollama:Endpoint", ollama)
            .WithSetting("Koan:AI:Ollama:Endpoint", ollama)
            .WithSetting("Koan:AI:Provider", "Ollama");

        if (qdrant is not null)
        {
            builder = builder
                .WithSetting("Koan:Data:Vector:Provider", "Qdrant")
                .WithSetting("Koan:Data:Qdrant:Endpoint", qdrant);
        }

        await using var host = await builder
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var corpus = Rag.Corpus<Fact>();
        corpus.Should().NotBeNull();

        // ── Ingest a tiny corpus ────────────────────────────────────────
        var doc = new Fact
        {
            Body = "The Agyo RAG stack ingests entity-native corpora and answers questions over them. " +
                   "Retrieval combines hybrid vector search with an emergent concept graph."
        };

        var ingest = await corpus.Ingest(doc, CancellationToken.None);
        ingest.Should().NotBeNull();
        ingest.Errors.Should().BeEmpty("ingestion over a tiny in-memory corpus should not error when AI infra is live");

        _output.WriteLine(
            $"Ingest: files={ingest.FilesProcessed}, chunks={ingest.ChunksCreated}, entities={ingest.EntitiesExtracted}");

        // ── Query ───────────────────────────────────────────────────────
        var vectorReady = await corpus.IsReady(CancellationToken.None);
        _output.WriteLine($"Vector backend ready: {vectorReady}");

        if (!vectorReady)
        {
            // No vector backend (only AI infra): the corpus is empty by contract, so Ask throws
            // RagCorpusEmptyException and AskResult reports EmptyCorpus. Assert that, don't fabricate.
            var emptyResult = await corpus.AskResult(
                "What does the Agyo RAG stack do?", CancellationToken.None);
            emptyResult.Status.Should().Be(RagQueryStatus.EmptyCorpus,
                $"no vector backend was provided — set {QdrantEnv} to exercise the full retrieval path");

            await FluentActions
                .Awaiting(() => corpus.Ask("What does the Agyo RAG stack do?", CancellationToken.None))
                .Should().ThrowAsync<RagCorpusEmptyException>();
            return;
        }

        // Full path: vector backend present — a real retrieve-and-generate answer.
        var result = await corpus.AskResult(
            "What does the Agyo RAG stack do?", CancellationToken.None);

        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(RagQueryStatus.Success, RagQueryStatus.NoResults);

        if (result.Status == RagQueryStatus.Success)
        {
            result.Answer.Should().NotBeNullOrWhiteSpace(
                "a successful retrieval over the ingested corpus must produce a generated answer");
            _output.WriteLine($"Answer: {result.Answer}");
        }
    }
}
