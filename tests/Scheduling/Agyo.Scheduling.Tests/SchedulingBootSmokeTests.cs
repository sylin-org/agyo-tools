using Agyo.Testing.Integration;
using AwesomeAssertions;
using Koan.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.Scheduling.Tests;

/// <summary>
/// ARCH-0079 boot-smoke: a REAL <c>AddKoan()</c> reflective bootstrap (via <see cref="AgyoIntegrationHost"/>,
/// a genuine <c>IHost</c> with hosted services started) must discover the Scheduling capability's
/// <c>KoanAutoRegistrar</c> and register the <c>SchedulingOrchestrator</c>. The only service wiring is
/// <c>s.AddKoan()</c>, so the orchestrator type being resolvable from the <c>Agyo.Scheduling</c>
/// assembly proves the registrar was found through reflective discovery (the orchestrator type is
/// <c>internal</c>, hence matched by assembly + name). The orchestrator is a
/// <c>[KoanBackgroundService]</c>, so Koan's own <c>KoanBackgroundServiceOrchestrator</c> owns its
/// lifecycle (it is intentionally not also registered as a top-level <see cref="IHostedService"/>,
/// which would run it twice).
/// </summary>
public sealed class SchedulingBootSmokeTests
{
    private const string OrchestratorTypeName = "SchedulingOrchestrator";
    private const string SchedulingAssemblyName = "Agyo.Scheduling";
    private const string KoanBackgroundOrchestratorTypeName = "KoanBackgroundServiceOrchestrator";

    private readonly ITestOutputHelper _output;

    public SchedulingBootSmokeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task AddKoan_DiscoversRegistrar_AndRegistersOrchestrator()
    {
        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(s => s.AddKoan())
            .StartAsync();

        // The registrar registers SchedulingOrchestrator as a singleton; reflective discovery found it
        // if it is resolvable from the Agyo.Scheduling assembly.
        var orchestratorType = typeof(IScheduledTask).Assembly
            .GetTypes()
            .FirstOrDefault(t => t.Name == OrchestratorTypeName);

        orchestratorType.Should().NotBeNull(
            "the Agyo.Scheduling assembly must contain the SchedulingOrchestrator type");

        var orchestrator = host.Services.GetService(orchestratorType!);
        orchestrator.Should().NotBeNull(
            "AddKoan() reflective discovery must find Agyo.Scheduling's KoanAutoRegistrar and " +
            "register the SchedulingOrchestrator in DI");
        orchestrator!.GetType().Assembly.GetName().Name.Should().Be(SchedulingAssemblyName);

        // And Koan's background-service orchestrator must be present to host [KoanBackgroundService]s.
        var hostedServices = host.Services.GetServices<IHostedService>().ToList();
        hostedServices.Should().Contain(
            svc => svc.GetType().Name == KoanBackgroundOrchestratorTypeName,
            "Koan's KoanBackgroundServiceOrchestrator owns the SchedulingOrchestrator lifecycle");

        _output.WriteLine($"Resolved scheduling orchestrator from DI: {orchestrator.GetType().FullName}");
    }
}
