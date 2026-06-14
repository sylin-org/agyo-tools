using System.Linq;
using AwesomeAssertions;
using Agyo.Data.Vector.PGVector;
using Agyo.Testing.Integration;
using Koan.Core;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.PGVector.Tests;

/// <summary>
/// ARCH-0079 boot smoke: a real <c>AddKoan()</c> reflective bootstrap must discover the PGVector
/// connector's <see cref="Agyo.Data.Vector.PGVector.Initialization.KoanAutoRegistrar"/> purely from the
/// package reference (Reference = Intent) and register <see cref="PGVectorAdapterFactory"/> as the
/// <see cref="IVectorAdapterFactory"/>. No hand-registration — the host runs genuine discovery.
/// </summary>
public sealed class PGVectorBootSmokeTests
{
    private readonly ITestOutputHelper _output;

    public PGVectorBootSmokeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task AddKoan_discovers_and_registers_the_pgvector_adapter_factory()
    {
        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var factory = host.Services.GetRequiredService<IVectorAdapterFactory>();

        factory.Should().BeOfType<PGVectorAdapterFactory>(
            "the PGVector KoanAutoRegistrar registers PGVectorAdapterFactory as IVectorAdapterFactory via reflective discovery");

        _output.WriteLine($"Resolved IVectorAdapterFactory = {factory.GetType().FullName}");
    }

    [Fact]
    public async Task Registered_factory_advertises_the_pgvector_provider()
    {
        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var factory = host.Services.GetRequiredService<IVectorAdapterFactory>();

        factory.Provider.Should().Be("pgvector");
        factory.CanHandle("pgvector").Should().BeTrue();
        factory.CanHandle("pg-vector").Should().BeTrue();
        factory.CanHandle("postgres-vector").Should().BeTrue();
        factory.CanHandle("qdrant").Should().BeFalse("the PGVector factory must not claim unrelated providers");
    }

    [Fact]
    public async Task PgVectorExtensionManager_is_registered_as_a_singleton()
    {
        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        // The registrar adds the extension manager as the shared version-detection singleton; it must
        // resolve and be the same instance on repeated resolution.
        var first = host.Services.GetRequiredService<PgVectorExtensionManager>();
        var second = host.Services.GetRequiredService<PgVectorExtensionManager>();

        first.Should().NotBeNull();
        first.Should().BeSameAs(second);
    }

    [Fact]
    public async Task Exactly_one_vector_adapter_factory_is_registered()
    {
        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var factories = host.Services.GetServices<IVectorAdapterFactory>().ToList();

        factories.Should().ContainSingle().Which.Should().BeOfType<PGVectorAdapterFactory>();
    }
}
