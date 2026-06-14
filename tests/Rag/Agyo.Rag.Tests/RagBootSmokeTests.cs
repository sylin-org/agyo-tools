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
/// ARCH-0079 boot-smoke for the Agyo.Rag capability. Boots a REAL <c>AddKoan()</c> host through
/// <see cref="Agyo.Testing.Integration.AgyoIntegrationHost"/> (hosted services start; genuine
/// reflective discovery of <c>KoanRagAutoRegistrar</c>) and asserts the RAG service surface
/// resolves — the DI-registered <see cref="IRagService"/> and the static <see cref="Rag"/> facade.
/// No external AI/Vector backend is required to register; the InMemory data adapter is enough.
/// <para>
/// NOTE: this assembly deliberately declares NO <c>[RagCorpus]</c>-decorated entity. The registrar
/// scans every loaded assembly for <c>[RagCorpus]</c> types and wires lifecycle hooks for each, and
/// that path currently throws at boot (see <see cref="RagCorpusLifecycleHookRegressionTests"/>).
/// The convention path (<c>Rag.Corpus&lt;T&gt;()</c> on an undecorated entity) needs no hooks, so
/// the service surface still boots and resolves cleanly.
/// </para>
/// <para>
/// Lives in the <c>RagAmbientHost</c> collection (non-parallel) because the static <see cref="Rag"/>
/// facade reads <c>IRagService</c> from the process-wide ambient <c>AppHost.Current</c>; running
/// these boots concurrently would let one test observe another's disposed host.
/// </para>
/// </summary>
[Collection("RagAmbientHost")]
public sealed class RagBootSmokeTests
{
    private readonly ITestOutputHelper _output;

    public RagBootSmokeTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// An undecorated corpus entity. Used via convention inference only — no <c>[RagCorpus]</c>,
    /// so the registrar wires no lifecycle hooks for it and the host boots cleanly.
    /// </summary>
    private sealed class SmokeDoc : Entity<SmokeDoc>
    {
        public string Body { get; set; } = string.Empty;
    }

    [Fact]
    public async Task AddKoan_resolves_the_RAG_service_surface()
    {
        await using var host = await Agyo.Testing.Integration.AgyoIntegrationHost.Configure()
            // Pin the default data provider so RAG's Entity<T> job ledger resolves deterministically.
            .WithSetting("Koan:Data:DefaultProvider", "inmemory")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        // 1. The capability's primary DI surface is registered via reflective discovery.
        var ragService = host.Services.GetService<IRagService>();
        ragService.Should().NotBeNull(
            "KoanRagAutoRegistrar must register IRagService when Agyo.Rag is referenced and AddKoan() runs");

        // 2. The static facade resolves the same service through the ambient AppHost.
        Rag.IsAvailable.Should().BeTrue(
            "the static Rag facade reads IRagService from the ambient AppHost set by the running host");

        // 3. A corpus is obtainable zero-config via convention inference, through the facade
        //    and equivalently through the host's own resolved service.
        var corpus = Rag.Corpus<SmokeDoc>();
        corpus.Should().NotBeNull();
        corpus.Should().BeAssignableTo<IRagCorpus<SmokeDoc>>();

        // Same (type, name) pair resolves to the same singleton corpus instance.
        Rag.Corpus<SmokeDoc>().Should().BeSameAs(corpus);
        ragService!.GetCorpus<SmokeDoc>().Should().BeSameAs(corpus,
            "the static facade and the DI service must hand back the same singleton corpus");

        // 4. The readiness surface works end-to-end: IsReady() flows through the ambient
        //    Vector<T>.IsAvailable facade and must return a definite value without throwing.
        //    Asserted here against the LIVE host (IsReady can't be isolated to host.Services; running
        //    it after the host disposes hits a disposed provider). The value itself depends on which
        //    vector adapter the referenced packages register, so we assert completion, not a fixed bool.
        var ready = await corpus.IsReady(CancellationToken.None);

        _output.WriteLine($"RAG service resolved: {ragService.GetType().FullName}");
        _output.WriteLine($"Default corpus EntityType: {corpus.EntityType.Name}");
        _output.WriteLine($"Vector backend ready: {ready}");
    }
}

/// <summary>
/// Non-parallel collection for specs that touch the process-wide ambient <c>AppHost.Current</c>
/// via the static <see cref="Rag"/> facade. Keeps ambient-host boots serialized.
/// </summary>
[CollectionDefinition("RagAmbientHost", DisableParallelization = true)]
public sealed class RagAmbientHostCollection;
