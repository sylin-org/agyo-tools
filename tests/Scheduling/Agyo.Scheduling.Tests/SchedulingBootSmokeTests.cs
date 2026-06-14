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
/// <c>KoanAutoRegistrar</c> and register the <c>SchedulingOrchestrator</c> as an
/// <see cref="IHostedService"/>. The only service wiring is <c>s.AddKoan()</c>, so a hosted
/// service from the <c>Agyo.Scheduling</c> assembly proves the registrar was found through reflective
/// discovery (the orchestrator type is <c>internal</c>, hence matched by assembly + name).
/// </summary>
public sealed class SchedulingBootSmokeTests
{
    private const string OrchestratorTypeName = "SchedulingOrchestrator";
    private const string SchedulingAssemblyName = "Agyo.Scheduling";

    private readonly ITestOutputHelper _output;

    public SchedulingBootSmokeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task AddKoan_DiscoversRegistrar_AndRegistersOrchestratorAsHostedService()
    {
        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(s => s.AddKoan())
            .StartAsync();

        var hostedServices = host.Services.GetServices<IHostedService>().ToList();

        var orchestrator = hostedServices.FirstOrDefault(svc =>
            svc.GetType().Name == OrchestratorTypeName &&
            svc.GetType().Assembly.GetName().Name == SchedulingAssemblyName);

        orchestrator.Should().NotBeNull(
            "AddKoan() reflective discovery must find Agyo.Scheduling's KoanAutoRegistrar and " +
            "register the SchedulingOrchestrator as an IHostedService");
        _output.WriteLine($"Resolved scheduling hosted service: {orchestrator!.GetType().FullName}");
    }
}
