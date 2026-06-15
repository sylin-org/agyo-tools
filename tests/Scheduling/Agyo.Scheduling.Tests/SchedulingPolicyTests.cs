using Agyo.Testing.Integration;
using AwesomeAssertions;
using Koan.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.Scheduling.Tests;

/// <summary>
/// ARCH-0079 behavioral specs for the policy interfaces the orchestrator now honors:
/// <see cref="ICronScheduled"/>, <see cref="IAllowedWindows"/>, and <see cref="IProvidesLock"/>.
/// Each boots a real <c>AddKoan()</c> reflective bootstrap via <see cref="AgyoIntegrationHost"/>
/// (a genuine <c>IHost</c> with hosted services started) and seeds
/// <c>Agyo:Scheduling:Enabled=true</c> because the "Test" environment defaults scheduling off.
/// </summary>
public sealed class SchedulingPolicyTests
{
    private static readonly TimeSpan FireTimeout = TimeSpan.FromSeconds(10);

    private readonly ITestOutputHelper _output;

    public SchedulingPolicyTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A task implementing <see cref="ICronScheduled"/> with a once-per-second expression
    /// (<c>* * * * * *</c>, the seconds-included Cronos form) must fire on its cron schedule.
    /// </summary>
    [Fact]
    public async Task CronTask_FiresOnSchedule_WithinTimeout()
    {
        var task = new CronProbeTask();

        await using var host = await AgyoIntegrationHost.Configure()
            .WithSetting("Agyo:Scheduling:Enabled", "true")
            .ConfigureServices(s =>
            {
                s.AddSingleton<IScheduledTask>(task);
                s.AddKoan();
            })
            .StartAsync();

        var fired = await Task.WhenAny(task.Fired, Task.Delay(FireTimeout)) == task.Fired;

        fired.Should().BeTrue(
            "the orchestrator must parse the task's cron expression and dispatch it on that schedule");
        _output.WriteLine($"Cron task '{task.Id}' fired: {fired}");
    }

    /// <summary>
    /// A task implementing <see cref="IAllowedWindows"/> whose declared window does NOT include the
    /// current time must be skipped on every occurrence during the check period. The task uses a fast
    /// fixed delay so it would otherwise run many times, isolating the window gate as the only reason
    /// it stays silent.
    /// </summary>
    [Fact]
    public async Task AllowedWindowsTask_OutsideWindow_DoesNotRun()
    {
        var task = new WindowProbeTask();

        await using var host = await AgyoIntegrationHost.Configure()
            .WithSetting("Agyo:Scheduling:Enabled", "true")
            .ConfigureServices(s =>
            {
                s.AddSingleton<IScheduledTask>(task);
                s.AddKoan();
            })
            .StartAsync();

        // Give the 1s poll loop several ticks to (not) fire the task.
        var ranWithinPeriod = await Task.WhenAny(task.Ran, Task.Delay(TimeSpan.FromSeconds(4))) == task.Ran;

        ranWithinPeriod.Should().BeFalse(
            "a task whose allowed window excludes the current time must be skipped, not run");
        task.RunCount.Should().Be(0, "no occurrence should have executed while outside the window");
        _output.WriteLine($"Window task '{task.Id}' runCount={task.RunCount} (expected 0)");
    }

    /// <summary>
    /// Two tasks implementing <see cref="IProvidesLock"/> with the SAME lock key must serialize: while
    /// one holds the named in-process lock, the other cannot enter its body. We observe overlap by
    /// tracking concurrent in-body count via a shared coordinator and assert it never exceeds 1.
    /// </summary>
    [Fact]
    public async Task ProvidesLockTasks_SharingKey_DoNotOverlap()
    {
        var coordinator = new LockCoordinator();
        var a = new LockProbeTask("lock-probe-a", coordinator);
        var b = new LockProbeTask("lock-probe-b", coordinator);

        await using var host = await AgyoIntegrationHost.Configure()
            .WithSetting("Agyo:Scheduling:Enabled", "true")
            .ConfigureServices(s =>
            {
                s.AddSingleton<IScheduledTask>(a);
                s.AddSingleton<IScheduledTask>(b);
                s.AddKoan();
            })
            .StartAsync();

        // Both are OnStartup, so both are dispatched immediately and contend for the shared lock.
        var bothRan = await Task.WhenAny(coordinator.BothEntered, Task.Delay(FireTimeout)) == coordinator.BothEntered;

        bothRan.Should().BeTrue("both lock-sharing tasks should run (serially) within the timeout");
        coordinator.MaxObservedConcurrency.Should().Be(1,
            "tasks sharing an IProvidesLock key must never execute concurrently");
        _output.WriteLine($"Lock tasks ran: maxConcurrency={coordinator.MaxObservedConcurrency} (expected 1)");
    }

    /// <summary>Cron task firing once per second; signals on first invocation.</summary>
    private sealed class CronProbeTask : IScheduledTask, ICronScheduled
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => "test:cron-probe";
        public string Cron => "* * * * * *"; // every second (6-field, seconds-included)
        public Task Fired => _tcs.Task;

        public Task Run(CancellationToken ct)
        {
            _tcs.TrySetResult();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Fixed-delay task gated to a window that excludes "now". The window is a one-minute slot two
    /// hours ahead in UTC, computed at construction so it never coincides with the test run window.
    /// </summary>
    private sealed class WindowProbeTask : IScheduledTask, IFixedDelay, IAllowedWindows
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly IReadOnlyList<(TimeSpan start, TimeSpan end)> _windows;
        private int _runCount;

        public WindowProbeTask()
        {
            // Pick a one-minute window two hours from now (UTC), guaranteed not to contain the current
            // time-of-day during a multi-second test, and not to wrap midnight relative to "now".
            var future = DateTimeOffset.UtcNow.AddHours(2).TimeOfDay;
            var windowStart = TimeSpan.FromHours(future.Hours).Add(TimeSpan.FromMinutes(future.Minutes));
            var windowEnd = windowStart.Add(TimeSpan.FromMinutes(1));
            _windows = new[] { (windowStart, windowEnd) };
        }

        public string Id => "test:window-probe";
        public TimeSpan Delay => TimeSpan.FromMilliseconds(500); // would fire often if not gated
        public IReadOnlyList<(TimeSpan start, TimeSpan end)> Windows => _windows;
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public int RunCount => Volatile.Read(ref _runCount);
        public Task Ran => _tcs.Task;

        public Task Run(CancellationToken ct)
        {
            Interlocked.Increment(ref _runCount);
            _tcs.TrySetResult();
            return Task.CompletedTask;
        }
    }

    /// <summary>Tracks concurrent in-body executions across lock-sharing tasks.</summary>
    private sealed class LockCoordinator
    {
        private readonly TaskCompletionSource _bothEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _current;
        private int _max;
        private int _enteredCount;

        public Task BothEntered => _bothEntered.Task;
        public int MaxObservedConcurrency => Volatile.Read(ref _max);

        public void Enter()
        {
            var now = Interlocked.Increment(ref _current);
            // Track the high-water mark of simultaneous in-body executions.
            int observed;
            do { observed = Volatile.Read(ref _max); }
            while (now > observed && Interlocked.CompareExchange(ref _max, now, observed) != observed);

            if (Interlocked.Increment(ref _enteredCount) >= 2)
                _bothEntered.TrySetResult();
        }

        public void Exit() => Interlocked.Decrement(ref _current);
    }

    /// <summary>
    /// OnStartup task that holds the body briefly while recording overlap. Two instances share the
    /// same <see cref="IProvidesLock.LockName"/> so the orchestrator must serialize them.
    /// </summary>
    private sealed class LockProbeTask : IScheduledTask, IOnStartup, IProvidesLock
    {
        private readonly string _id;
        private readonly LockCoordinator _coordinator;

        public LockProbeTask(string id, LockCoordinator coordinator)
        {
            _id = id;
            _coordinator = coordinator;
        }

        public string Id => _id;
        public string LockName => "test:shared-lock";

        public async Task Run(CancellationToken ct)
        {
            _coordinator.Enter();
            try
            {
                // Hold long enough that an un-serialized peer would overlap on the 1s poll cadence.
                await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
            }
            finally
            {
                _coordinator.Exit();
            }
        }
    }
}
