using Agyo.Secrets.Abstractions;
using Agyo.Secrets.Core.Initialization;
using Agyo.Testing.Integration;
using AwesomeAssertions;
using Koan.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.Secrets.Tests;

/// <summary>
/// ARCH-0079 boot-smoke: a REAL <c>AddKoan()</c> reflective bootstrap (via <see cref="AgyoIntegrationHost"/>,
/// a genuine <c>IHost</c> with hosted services started) must discover the Secrets capability's
/// <c>KoanAutoRegistrar</c> and register <see cref="ISecretResolver"/>.
/// </summary>
/// <remarks>
/// The only service wiring is <c>s.AddKoan()</c> — the test never calls <c>AddKoanSecrets()</c> or
/// registers <see cref="ISecretResolver"/> by hand. Registration flows entirely through Koan's
/// reflective <c>RegistryManifestLoader</c> -> <c>KoanAutoRegistrar.Initialize</c>. That loader only
/// scans assemblies in the boot-time closure, and the CLR loads an assembly lazily — so a ProjectReference
/// alone is not enough to put <c>Agyo.Secrets.Core</c> in the closure unless one of its types is touched
/// (the same reason a real host must reference the capability package). <see cref="BootSmoke.EnsureLoaded"/>
/// performs that touch with a no-op <c>typeof</c>; it does NOT register anything. This mirrors how a
/// production host pulls the capability into its reference closure.
/// </remarks>
public sealed class SecretsBootSmokeTests
{
    private readonly ITestOutputHelper _output;

    public SecretsBootSmokeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task AddKoan_DiscoversRegistrar_AndResolvesSecretResolver()
    {
        BootSmoke.EnsureCapabilityAssemblyLoaded();

        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(s => s.AddKoan())
            .StartAsync();

        var resolver = host.Services.GetService<ISecretResolver>();

        resolver.Should().NotBeNull(
            "AddKoan() reflective discovery must find Agyo.Secrets.Core's KoanAutoRegistrar and register ISecretResolver");
        _output.WriteLine($"Resolved ISecretResolver implementation: {resolver!.GetType().FullName}");
    }
}

/// <summary>
/// Test-side helper that puts the capability assembly into the CLR-loaded set before bootstrap, so
/// Koan's reflective discovery can scan it. A <c>typeof</c> reference forces assembly load without
/// invoking any registration code — discovery still does all the work.
/// </summary>
internal static class BootSmoke
{
    /// <summary>Reference a capability type so the CLR loads <c>Agyo.Secrets.Core</c>. No registration happens here.</summary>
    public static void EnsureCapabilityAssemblyLoaded() => EnsureLoaded(typeof(KoanAutoRegistrar));

    private static void EnsureLoaded(Type marker) => _ = marker.Assembly.FullName;
}
