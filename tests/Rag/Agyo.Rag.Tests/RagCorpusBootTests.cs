using Agyo.Rag;
using Agyo.Rag.Abstractions;
using AwesomeAssertions;
using Koan.Core;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.Rag.Tests;

/// <summary>
/// ARCH-0079 integration spec for the <c>[RagCorpus]</c> auto-ingest registration path — the exact
/// path that the migration bug broke (see <see cref="RagCorpusLifecycleHookRegressionTests"/>).
/// <para>
/// <see cref="Policy"/> is a <c>[RagCorpus]</c>-decorated <c>Entity&lt;Policy&gt;</c>. Booting a real
/// <c>AddKoan()</c> host drives <c>KoanRagAutoRegistrar</c> through its full lifecycle-hook wiring for
/// this type: <c>DiscoverRagCorpusTypes</c> finds it, <c>RegisterIngestionHooks</c> resolves the
/// inherited static <c>Events</c> property and wires <c>AfterUpsert</c>/<c>AfterRemove</c>, and
/// <c>RegisterJobProcessor</c> registers its typed processor. Before the FlattenHierarchy fix this
/// threw <c>"Entity base type for Policy has no static Events property"</c> and aborted <c>AddKoan()</c>;
/// this test asserts it now boots cleanly end-to-end.
/// </para>
/// <para>
/// NOTE: <c>DiscoverRagCorpusTypes</c> scans every loaded assembly, so this single decorated
/// <see cref="Policy"/> is also discovered by the other <c>AddKoan()</c> boots in this assembly
/// (boot-smoke, behavioral). Post-fix that registration succeeds, so it no longer poisons those boots.
/// </para>
/// <para>
/// Joins the non-parallel <c>RagAmbientHost</c> collection because it boots a host that publishes the
/// process-wide ambient <c>AppHost.Current</c> the static <see cref="Rag"/> facade reads from.
/// </para>
/// </summary>
[Collection("RagAmbientHost")]
public sealed class RagCorpusBootTests
{
    private readonly ITestOutputHelper _output;

    public RagCorpusBootTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A <c>[RagCorpus]</c>-decorated entity exercising the auto-ingest registration path. Uses the
    /// default (unnamed) corpus, synchronous lifecycle (<c>Async = false</c>) so no background job
    /// ledger write is implied at registration, and a string-keyed <c>Entity&lt;Policy&gt;</c> as the
    /// registrar requires.
    /// </summary>
    [RagCorpus(Async = false)]
    private sealed class Policy : Entity<Policy>
    {
        public string Body { get; set; } = string.Empty;
    }

    [Fact]
    public async Task AddKoan_boots_cleanly_with_a_RagCorpus_decorated_entity()
    {
        // Booting through AddKoan() runs KoanRagAutoRegistrar.Initialize, which discovers the
        // [RagCorpus] Policy entity and wires its lifecycle hooks. Before the fix this threw inside
        // StartAsync()'s reflective bootstrap; the await alone is the assertion that it no longer does.
        // RagAmbientHostScope resets the ambient AppHost.Current on teardown so this real boot doesn't
        // leave a disposed provider behind for the other ambient-host specs in this collection.
        await using var host = await RagAmbientHostScope.StartAsync(
            Agyo.Testing.Integration.AgyoIntegrationHost.Configure()
                // Pin the default data provider so RAG's Entity<T> job ledger resolves deterministically.
                .WithSetting("Koan:Data:DefaultProvider", "inmemory")
                .ConfigureServices(services => services.AddKoan()));

        // The capability surface still registers with the decorated entity present.
        var ragService = host.Services.GetService<IRagService>();
        ragService.Should().NotBeNull(
            "the [RagCorpus] auto-ingest registration path must complete so IRagService is registered");

        // The decorated entity's corpus is obtainable — the hook-wired type resolves a real corpus.
        var corpus = Rag.Corpus<Policy>();
        corpus.Should().NotBeNull();
        corpus.Should().BeAssignableTo<IRagCorpus<Policy>>();
        corpus.EntityType.Should().Be(typeof(Policy));

        // The [RagCorpus] declaration is what flips lifecycle wiring on — assert the metadata the
        // registrar acted on so this test fails loudly if the attribute stops being honored.
        var metadata = RagCorpusMetadata.ResolveDefault<Policy>();
        metadata.LifecycleEnabled.Should().BeTrue(
            "the [RagCorpus] attribute on Policy enables the lifecycle hooks the registrar wires at boot");

        _output.WriteLine($"RAG service resolved: {ragService!.GetType().FullName}");
        _output.WriteLine($"[RagCorpus] entity corpus EntityType: {corpus.EntityType.Name}");
        _output.WriteLine($"LifecycleEnabled: {metadata.LifecycleEnabled}");
    }
}
