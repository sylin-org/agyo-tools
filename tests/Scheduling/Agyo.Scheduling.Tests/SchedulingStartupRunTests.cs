using Agyo.Testing.Integration;
using AwesomeAssertions;
using Koan.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.Scheduling.Tests;

/// <summary>
/// ARCH-0079 behavioral spec (no external infra): a real <c>AddKoan()</c> bootstrap discovers the
/// Scheduling orchestrator and runs it as a hosted service. We register a test
/// <see cref="IScheduledTask"/> that also implements <see cref="IOnStartup"/>; on host start the
/// orchestrator must invoke <see cref="IScheduledTask.Run"/> once, which we observe by awaiting a
/// <see cref="TaskCompletionSource"/> the task completes from inside <c>Run</c>.
/// </summary>
/// <remarks>
/// The orchestrator's <c>ExecuteCore</c> early-returns when <c>SchedulingOptions.Enabled</c> is
/// false. In the "Test" environment the registrar's PostConfigure disables scheduling unless the
/// <c>Agyo:Scheduling</c> section exists, so the spec seeds <c>Agyo:Scheduling:Enabled=true</c>.
/// </remarks>
public sealed class SchedulingStartupRunTests
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(5);

    private readonly ITestOutputHelper _output;

    public SchedulingStartupRunTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Orchestrator_RunsOnStartupTask_OnHostStart()
    {
        var task = new StartupProbeTask();

        await using var host = await AgyoIntegrationHost.Configure()
            .WithSetting("Agyo:Scheduling:Enabled", "true")
            .ConfigureServices(s =>
            {
                s.AddSingleton<IScheduledTask>(task);
                s.AddKoan();
            })
            .StartAsync();

        var ran = await Task.WhenAny(task.Ran, Task.Delay(RunTimeout)) == task.Ran;

        ran.Should().BeTrue(
            "the SchedulingOrchestrator (discovered via AddKoan() reflective bootstrap) must invoke " +
            "Run() on an OnStartup IScheduledTask within the startup window");
        _output.WriteLine($"OnStartup task '{task.Id}' ran: {ran}");
    }

    /// <summary>
    /// A minimal scheduled task that signals when the orchestrator invokes <c>Run</c>. Implements
    /// <see cref="IOnStartup"/> so the orchestrator schedules it for an immediate startup run.
    /// </summary>
    private sealed class StartupProbeTask : IScheduledTask, IOnStartup
    {
        private readonly TaskCompletionSource _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => "test:startup-probe";

        public Task Ran => _tcs.Task;

        public Task Run(CancellationToken ct)
        {
            _tcs.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
