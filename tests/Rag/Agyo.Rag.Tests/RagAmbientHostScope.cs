using Agyo.Testing.Integration;
using Koan.Core.Hosting.App;

namespace Agyo.Rag.Tests;

/// <summary>
/// Disposable wrapper that boots an integration host AND resets the ambient
/// <see cref="AppHost.Current"/> global on teardown.
/// <para>
/// WHY THIS EXISTS: Koan's host binder sets <c>AppHost.Current = sp</c> on first boot but only
/// <em>"if (AppHost.Current is null)"</em>, and nothing clears it when the host disposes
/// (<c>AppHostBinderHostedService</c>). So the FIRST ambient-host boot in a non-parallel collection
/// wins the global, and every later boot in that collection is ignored — leaving the static
/// <see cref="Rag"/> facade reading a <em>disposed</em> provider once the first test tears down,
/// which throws <see cref="System.ObjectDisposedException"/>. With a single real boot (the others
/// skip) this never surfaced; adding a second real boot (the <c>[RagCorpus]</c> auto-ingest spec)
/// exposes it.
/// </para>
/// <para>
/// This helper closes that gap purely on the test side: it nulls <see cref="AppHost.Current"/> after
/// the host disposes so the NEXT boot in the collection re-binds the ambient to its own (live)
/// provider. Each ambient-host spec in this assembly boots through this wrapper so they no longer
/// poison one another regardless of execution order.
/// </para>
/// </summary>
internal sealed class RagAmbientHostScope : IAsyncDisposable
{
    private readonly IntegrationHost _host;
    private bool _disposed;

    private RagAmbientHostScope(IntegrationHost host) => _host = host;

    /// <summary>The DI container produced by the booted host.</summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>Boot the supplied builder and wrap it for ambient-safe teardown.</summary>
    public static async Task<RagAmbientHostScope> StartAsync(
        AgyoIntegrationHost.Builder builder, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var host = await builder.StartAsync(ct).ConfigureAwait(false);
        return new RagAmbientHostScope(host);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _host.DisposeAsync().ConfigureAwait(false);

        // The disposed provider must not stay published as the ambient global, or the next
        // ambient-host boot's facade reads a disposed IServiceProvider. Clearing it lets Koan's
        // host binder re-bind AppHost.Current to the next live provider.
        AppHost.Current = null;
    }
}
