using System.Net.Http;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Koan.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Agyo.Observability;
using Agyo.Testing.Integration;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.Observability.Tests;

/// <summary>
/// ARCH-0079 integration suite for the Observability capability. Every spec boots a real
/// <see cref="Microsoft.Extensions.Hosting.IHost"/> through <see cref="AgyoIntegrationHost"/> with
/// <c>services.AddKoan()</c>, so the <see cref="ObservabilityModule"/> (a <c>KoanModule</c>) is
/// discovered + run by the framework's reflective bootstrap — not hand-registered here. The
/// assertions target the two surfaces the module's <c>Register()</c> establishes:
/// the ASP.NET Core <see cref="HealthCheckService"/> and the resilient named
/// <see cref="HttpClient"/> (<c>"agyo-observability"</c>).
/// </summary>
public sealed class ObservabilityModuleIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public ObservabilityModuleIntegrationTests(ITestOutputHelper output)
    {
        _output = output;

        // Discovery uses a runtime AppDomain.GetAssemblies() scan (the Observability capability
        // project ships no compile-time registry manifest — it does not reference
        // Sylin.Koan.Core.Registry.Generators). On a cold run .NET has not yet lazily loaded the
        // Agyo.Observability assembly when the first AddKoan() scan executes, so the module would
        // be missed. Force the assembly into the AppDomain before any boot. In a real app the
        // hosting Program.cs reference loads it at startup; this reproduces that guarantee for the
        // test host so the boot-smoke deterministically exercises reflective discovery. Touching a
        // member of the type forces the JIT/loader to bring its defining assembly into the AppDomain.
        RuntimeHelpers.RunClassConstructor(typeof(ObservabilityModule).TypeHandle);
        _ = ObservabilityModule.HttpClientName;
    }

    /// <summary>
    /// BOOT-SMOKE (mandatory ARCH-0079): a real <c>AddKoan()</c> boot must wire the health-checks
    /// baseline the module registers. Resolving <see cref="HealthCheckService"/> proves the
    /// <see cref="ObservabilityModule"/> ran through reflective discovery — nothing in this test
    /// registers it by hand.
    /// </summary>
    [Fact]
    public async Task AddKoan_boots_and_wires_the_health_check_service()
    {
        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var healthChecks = host.Services.GetService<HealthCheckService>();

        healthChecks.Should().NotBeNull(
            "the ObservabilityModule's Register() calls AddHealthChecks(), which the real " +
            "AddKoan() reflective discovery must have run");

        _output.WriteLine($"HealthCheckService resolved: {healthChecks!.GetType().FullName}");
    }

    /// <summary>
    /// BEHAVIORAL (no infra): the module registers a resilient named HttpClient. A real boot must
    /// produce a usable client from <see cref="IHttpClientFactory.CreateClient(string)"/> for the
    /// <see cref="ObservabilityModule.HttpClientName"/> name, and that named client must carry the
    /// standard resilience handler the module attached (observable as a registered
    /// <see cref="HttpClientFactoryOptions.HttpMessageHandlerBuilderActions"/> entry — a plain,
    /// unconfigured name would have none).
    /// </summary>
    [Fact]
    public async Task AddKoan_registers_the_resilient_named_http_client()
    {
        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var factory = host.Services.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(ObservabilityModule.HttpClientName);

        client.Should().NotBeNull(
            "IHttpClientFactory must produce the named client the ObservabilityModule registered");

        // Prove the name was actually configured (resilience handler attached) rather than the
        // factory silently handing back a default client for an unknown name.
        var optionsMonitor = host.Services
            .GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>();
        var named = optionsMonitor.Get(ObservabilityModule.HttpClientName);

        named.HttpMessageHandlerBuilderActions.Should().NotBeEmpty(
            "AddStandardResilienceHandler() on the \"agyo-observability\" client registers a " +
            "handler-builder action; an unconfigured client name would have none");

        _output.WriteLine(
            $"Named client '{ObservabilityModule.HttpClientName}' configured with " +
            $"{named.HttpMessageHandlerBuilderActions.Count} handler-builder action(s)");
    }
}
