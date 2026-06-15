using Agyo.Testing.Integration;
using Koan.Core.Hosting.App;

namespace Agyo.Service.Librarian.Tests;

/// <summary>
/// Disposable wrapper that boots an integration host AND resets the ambient
/// <see cref="AppHost.Current"/> global on teardown — the same pattern Agyo.Rag.Tests uses.
/// <para>
/// WHY: Koan's host binder publishes <c>AppHost.Current = sp</c> on first boot and only
/// <em>"if (AppHost.Current is null)"</em>; nothing clears it when the host disposes. The Librarian
/// leans on the ambient host heavily (every static <c>Entity&lt;T&gt;</c> facade — Project, Job,
/// Chunk — and the seeder/scheduled-task hosted services read it). Without a reset, the first
/// ambient-host boot in a non-parallel collection wins the global and later boots read a
/// <em>disposed</em> provider once it tears down. This wrapper nulls it after disposal so the next
/// boot re-binds to its own live provider.
/// </para>
/// </summary>
internal sealed class LibrarianHostScope : IAsyncDisposable
{
    private readonly IntegrationHost _host;
    private bool _disposed;

    private LibrarianHostScope(IntegrationHost host) => _host = host;

    /// <summary>The DI container produced by the booted host.</summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>Boot the supplied builder and wrap it for ambient-safe teardown.</summary>
    public static async Task<LibrarianHostScope> StartAsync(
        AgyoIntegrationHost.Builder builder, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var host = await builder.StartAsync(ct).ConfigureAwait(false);
        return new LibrarianHostScope(host);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _host.DisposeAsync().ConfigureAwait(false);

        // Clear the disposed provider from the ambient global so the next ambient-host boot re-binds.
        AppHost.Current = null;
    }
}
