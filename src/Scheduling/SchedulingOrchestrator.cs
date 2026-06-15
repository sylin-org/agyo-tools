using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Cronos;
using Koan.Core;
using Koan.Core.BackgroundServices;
using Koan.Core.Observability.Health;

namespace Agyo.Scheduling;

[KoanBackgroundService(RunInProduction = true)]
[ServiceEvent(Koan.Core.Events.KoanServiceEvents.Scheduling.TaskExecuted, EventArgsType = typeof(TaskExecutedEventArgs))]
[ServiceEvent(Koan.Core.Events.KoanServiceEvents.Scheduling.TaskFailed, EventArgsType = typeof(TaskFailedEventArgs))]
[ServiceEvent(Koan.Core.Events.KoanServiceEvents.Scheduling.TaskTimeout, EventArgsType = typeof(TaskTimeoutEventArgs))]
internal sealed class SchedulingOrchestrator : KoanFluentServiceBase
{
    private readonly IOptionsMonitor<SchedulingOptions> _options;
    private readonly IEnumerable<IScheduledTask> _tasks;
    private readonly Koan.Core.Observability.Health.IHealthAggregator _health;
    private readonly IHostEnvironment _env;
    private readonly List<Runner> _runners = new();

    // Named in-process lock registry (IProvidesLock). Tasks declaring the same LockName share one
    // SemaphoreSlim(1,1) and therefore serialize across runners. This is the correct scope for the
    // lightweight in-proc scheduler — the durable/distributed lock case is Koan.Jobs (out of scope).
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _namedLocks = new(StringComparer.Ordinal);

    // Re-entrancy guard (defense-in-depth). Registration ensures a single owner (Koan's
    // KoanBackgroundServiceOrchestrator), but this also makes the build+poll loop idempotent per
    // instance so a stray second ExecuteCore invocation can never fire every task twice.
    // 0 = not started, 1 = started.
    private int _executeStarted;

    public SchedulingOrchestrator(
        ILogger<SchedulingOrchestrator> logger,
        IConfiguration configuration,
        IOptionsMonitor<SchedulingOptions> options,
        IEnumerable<IScheduledTask> tasks,
        Koan.Core.Observability.Health.IHealthAggregator health,
        IHostEnvironment env)
        : base(logger, configuration)
    {
        _options = options;
        _tasks = tasks;
        _health = health;
        _env = env;
    }

    public override async Task ExecuteCore(CancellationToken stoppingToken)
    {
        // Run the build+poll loop at most once per instance even if multiple host paths invoke it.
        if (Interlocked.Exchange(ref _executeStarted, 1) != 0)
        {
            Logger.LogDebug("Scheduling orchestrator ExecuteCore invoked again; ignoring (already running)");
            return;
        }

        await Task.Yield();
        var opts = _options.CurrentValue;
        if (!opts.Enabled)
        {
            Logger.LogInformation("Scheduling disabled (env: {Env})", _env.EnvironmentName);
            return;
        }

        Logger.LogInformation("Starting scheduling orchestrator - building task runners");

        // Build runners
        foreach (var t in _tasks)
        {
            var job = BuildJob(t, opts);
            if (job is null) continue;
            _runners.Add(job);
        }

        Logger.LogInformation("Scheduling orchestrator started with {RunnerCount} task runners", _runners.Count);

        // Start
        var startup = _runners.Where(r => r.OnStartup).ToList();
        foreach (var r in startup)
            _ = r.RunOnce(stoppingToken);

        // Poll loop: dispatches both fixed-delay and cron runners. A runner computes its own
        // NextRunUtc (from its FixedDelay or cron expression); the loop simply fires anything due.
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var due = _runners.Where(r => r.NextRunUtc is not null && r.NextRunUtc <= now).ToList();
            foreach (var r in due)
                _ = r.RunOnce(stoppingToken);

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }

        Logger.LogInformation("Scheduling orchestrator stopped");
    }

    [ServiceAction(Koan.Core.Actions.KoanServiceActions.Scheduling.TriggerTask)]
    public Task TriggerTaskAction(string taskId, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Manual task trigger requested for: {TaskId}", taskId);

        var runner = _runners.FirstOrDefault(r => r.Id == taskId);
        if (runner != null)
        {
            _ = runner.RunOnce(cancellationToken);
            Logger.LogInformation("Task {TaskId} triggered successfully", taskId);
        }
        else
        {
            Logger.LogWarning("Task {TaskId} not found in runners", taskId);
        }
        return Task.CompletedTask;
    }

    [ServiceAction(Koan.Core.Actions.KoanServiceActions.Scheduling.ListTasks)]
    public Task ListTasksAction(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Listing all scheduled tasks:");
        foreach (var runner in _runners)
        {
            Logger.LogInformation("Task: {TaskId}, OnStartup: {OnStartup}, NextRun: {NextRun}",
                runner.Id, runner.OnStartup, runner.NextRunUtc);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolve (creating if needed) the shared <see cref="SemaphoreSlim"/> for a named lock so that
    /// every runner declaring the same <see cref="IProvidesLock.LockName"/> contends on one gate.
    /// </summary>
    private SemaphoreSlim GetNamedLock(string lockName)
        => _namedLocks.GetOrAdd(lockName, static _ => new SemaphoreSlim(1, 1));

    private Runner? BuildJob(IScheduledTask task, SchedulingOptions opts)
    {
        var id = task.Id;
        var jobOpts = opts.Jobs.TryGetValue(id, out var j) ? j : null;

        // Merge triggers and policies: config > attribute > interface defaults
        var attr = task.GetType().GetCustomAttributes(typeof(ScheduledAttribute), false).FirstOrDefault() as ScheduledAttribute;

        bool enabled = jobOpts?.Enabled ?? true;
        if (!enabled) return null;

        bool onStartup = jobOpts?.OnStartup ?? attr?.OnStartup ?? task is IOnStartup;

        TimeSpan? fixedDelay = jobOpts?.FixedDelay;
        if (fixedDelay is null && attr?.FixedDelaySeconds is int s) fixedDelay = TimeSpan.FromSeconds(s);
        if (fixedDelay is null && task is IFixedDelay fd) fixedDelay = fd.Delay;

        // Cron trigger: config > attribute > interface. Parsed once here so a malformed expression
        // fails loudly at build time rather than silently never firing.
        var cronSchedule = ResolveCronSchedule(task, attr, jobOpts);

        bool critical = jobOpts?.Critical ?? attr?.Critical ?? (task is IIsCritical);

        TimeSpan? timeout = jobOpts?.Timeout;
        if (timeout is null && attr?.TimeoutSeconds is int ts) timeout = TimeSpan.FromSeconds(ts);
        if (timeout is null && task is IHasTimeout to) timeout = to.Timeout;

        int maxConc = jobOpts?.MaxConcurrency ?? attr?.MaxConcurrency ?? (task is IHasMaxConcurrency mc ? mc.MaxConcurrency : 1);

        // Allowed windows (IAllowedWindows): occurrences outside any window are skipped.
        var windows = task as IAllowedWindows;

        // Named in-process lock (IProvidesLock): shared gate so tasks with the same key serialize.
        SemaphoreSlim? namedLock = null;
        if (task is IProvidesLock providesLock && !string.IsNullOrWhiteSpace(providesLock.LockName))
            namedLock = GetNamedLock(providesLock.LockName);

        // Health facts (IHealthFacts): per-run, merged into the pushed health snapshot.
        var healthFacts = task as IHealthFacts;

        return new Runner(task, _health, id, onStartup, fixedDelay, cronSchedule, windows, namedLock, healthFacts, critical, timeout, maxConc, this);
    }

    /// <summary>
    /// Resolve the cron schedule for a task (config &gt; attribute &gt; interface), or <c>null</c> when
    /// the task declares no cron trigger. The 6-field form (with a leading seconds field) is detected
    /// automatically so fast expressions like <c>* * * * * *</c> work.
    /// </summary>
    private static CronSchedule? ResolveCronSchedule(IScheduledTask task, ScheduledAttribute? attr, SchedulingOptions.JobOptions? jobOpts)
    {
        string? expr = jobOpts?.Cron ?? attr?.Cron;
        TimeZoneInfo tz = TimeZoneInfo.Utc;

        if (expr is null && task is ICronScheduled cron)
        {
            expr = cron.Cron;
            tz = cron.TimeZone;
        }

        if (string.IsNullOrWhiteSpace(expr)) return null;
        return CronSchedule.Parse(expr, tz);
    }

    /// <summary>
    /// Immutable wrapper over a parsed Cronos expression plus its time zone. Field count selects the
    /// format: 6 whitespace-separated fields =&gt; seconds-included, 5 =&gt; standard.
    /// </summary>
    private sealed class CronSchedule
    {
        private readonly CronExpression _expression;
        private readonly TimeZoneInfo _timeZone;

        private CronSchedule(CronExpression expression, TimeZoneInfo timeZone)
        {
            _expression = expression;
            _timeZone = timeZone;
        }

        public static CronSchedule Parse(string expression, TimeZoneInfo timeZone)
        {
            var fieldCount = expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            var format = fieldCount >= 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
            var parsed = CronExpression.Parse(expression, format);
            return new CronSchedule(parsed, timeZone);
        }

        /// <summary>Next occurrence strictly after <paramref name="afterUtc"/>, in UTC, or null if none.</summary>
        public DateTimeOffset? Next(DateTimeOffset afterUtc)
        {
            var next = _expression.GetNextOccurrence(afterUtc, _timeZone, inclusive: false);
            return next;
        }
    }

    private sealed class Runner
    {
        private readonly IScheduledTask task;
        private readonly Koan.Core.Observability.Health.IHealthAggregator health;
        private readonly TimeSpan? timeout;
        private readonly TimeSpan? fixedDelay;
        private readonly CronSchedule? cron;
        private readonly IAllowedWindows? windows;
        private readonly SemaphoreSlim? namedLock;
        private readonly IHealthFacts? healthFacts;
        private readonly bool critical;
        private readonly SchedulingOrchestrator orchestrator;
        private readonly SemaphoreSlim _gate;
        public string Id { get; }
        public bool OnStartup { get; }
        public DateTimeOffset? NextRunUtc { get; private set; }
        private int _running;
        private int _success;
        private int _fail;
        private int _skippedWindow;
        private string? _lastError;

        public Runner(
            IScheduledTask task,
            Koan.Core.Observability.Health.IHealthAggregator health,
            string id,
            bool onStartup,
            TimeSpan? fixedDelay,
            CronSchedule? cron,
            IAllowedWindows? windows,
            SemaphoreSlim? namedLock,
            IHealthFacts? healthFacts,
            bool critical,
            TimeSpan? timeout,
            int maxConcurrency,
            SchedulingOrchestrator orchestrator)
        {
            this.task = task;
            this.health = health;
            this.timeout = timeout;
            this.fixedDelay = fixedDelay;
            this.cron = cron;
            this.windows = windows;
            this.namedLock = namedLock;
            this.healthFacts = healthFacts;
            this.critical = critical;
            this.orchestrator = orchestrator;
            var cap = maxConcurrency <= 0 ? 1 : maxConcurrency;
            _gate = new SemaphoreSlim(cap, cap);
            Id = id;
            OnStartup = onStartup;
            // Seed the schedule. Cron takes precedence for the recurring NextRunUtc; otherwise
            // fixed-delay. A task with neither (e.g. OnStartup-only) has no scheduled NextRunUtc.
            NextRunUtc = ComputeNextRun(DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Next scheduled run after <paramref name="fromUtc"/>: cron occurrence if a cron schedule is
        /// declared, else fixed-delay offset, else null (no recurring schedule).
        /// </summary>
        private DateTimeOffset? ComputeNextRun(DateTimeOffset fromUtc)
        {
            if (cron is not null) return cron.Next(fromUtc);
            if (fixedDelay is not null) return fromUtc + fixedDelay;
            return null;
        }

        /// <summary>
        /// True when <paramref name="nowUtc"/> falls inside at least one declared allowed window.
        /// Windows are expressed in the task's TimeZone local clock; a window whose end is less than
        /// or equal to its start is treated as wrapping across midnight. No windows declared =&gt; always allowed.
        /// </summary>
        private bool IsWithinAllowedWindow(DateTimeOffset nowUtc)
        {
            if (windows is null) return true;
            var list = windows.Windows;
            if (list is null || list.Count == 0) return true;

            var tz = windows.TimeZone ?? TimeZoneInfo.Utc;
            var local = TimeZoneInfo.ConvertTime(nowUtc, tz).TimeOfDay;

            foreach (var (start, end) in list)
            {
                if (start <= end)
                {
                    // Same-day window, e.g. 09:00..17:00.
                    if (local >= start && local < end) return true;
                }
                else
                {
                    // Wrapping window, e.g. 22:00..06:00 (spans midnight).
                    if (local >= start || local < end) return true;
                }
            }
            return false;
        }

        public async Task RunOnce(CancellationToken ct)
        {
            SemaphoreSlim? gateHeld = null;
            try
            {
                if (!await _gate.WaitAsync(0, ct)) return; // max 1 schedule trigger at a time
                gateHeld = _gate;

                // Allowed-windows gate: if outside every declared window, skip this occurrence and let
                // the normal schedule re-evaluate on the next tick. We do NOT consume the named lock or
                // touch _running for a skipped occurrence.
                if (!IsWithinAllowedWindow(DateTimeOffset.UtcNow))
                {
                    Interlocked.Increment(ref _skippedWindow);
                    NextRunUtc = ComputeNextRun(DateTimeOffset.UtcNow);
                    health.Push($"scheduling:task:{Id}", Koan.Core.Observability.Health.HealthStatus.Healthy, message: "outside-window", ttl: TimeSpan.FromMinutes(5), facts: Facts("outside-window"));
                    return;
                }

                // Named in-process lock (IProvidesLock): acquire before executing so tasks sharing a
                // lock key serialize. Released in finally. WaitAsync honors cancellation/timeout via ct.
                bool lockHeld = false;
                if (namedLock is not null)
                {
                    await namedLock.WaitAsync(ct);
                    lockHeld = true;
                }
                try
                {
                    await ExecuteTask(ct);
                }
                finally
                {
                    if (lockHeld) namedLock!.Release();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Host is stopping: a fire-and-forget run must unwind quietly, never surface an
                // unobserved task exception.
            }
            finally
            {
                gateHeld?.Release();
            }
        }

        private async Task ExecuteTask(CancellationToken ct)
        {
            Interlocked.Increment(ref _running);
            var cts = timeout is not null ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
            if (cts is not null) cts.CancelAfter(timeout!.Value);
            var runCt = cts?.Token ?? ct;

            try
            {
                health.Push($"scheduling:task:{Id}", Koan.Core.Observability.Health.HealthStatus.Healthy, message: "running", ttl: TimeSpan.FromSeconds(30), facts: Facts("running"));
                var startedAt = DateTimeOffset.UtcNow;
                try
                {
                    await task.Run(runCt);
                    Interlocked.Increment(ref _success);
                    _lastError = null;
                    health.Push($"scheduling:task:{Id}", Koan.Core.Observability.Health.HealthStatus.Healthy, message: "ok", ttl: TimeSpan.FromMinutes(5), facts: Facts("ok"));

                    _ = orchestrator.EmitEvent(Koan.Core.Events.KoanServiceEvents.Scheduling.TaskExecuted, new TaskExecutedEventArgs
                    {
                        TaskId = Id,
                        ExecutedAt = DateTimeOffset.UtcNow,
                        Duration = DateTimeOffset.UtcNow - startedAt
                    });
                }
                catch (OperationCanceledException) when (runCt.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    // Timed out (our linked token fired, not the host's stopping token).
                    Interlocked.Increment(ref _fail);
                    _lastError = "timeout";
                    health.Push($"scheduling:task:{Id}", Koan.Core.Observability.Health.HealthStatus.Unhealthy, message: "timeout", ttl: TimeSpan.FromMinutes(5), facts: Facts("timeout"));

                    _ = orchestrator.EmitEvent(Koan.Core.Events.KoanServiceEvents.Scheduling.TaskTimeout, new TaskTimeoutEventArgs
                    {
                        TaskId = Id,
                        TimeoutAt = DateTimeOffset.UtcNow,
                        TimeoutDuration = timeout ?? TimeSpan.Zero
                    });
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Host is stopping — propagate so the run loop unwinds cleanly without recording a failure.
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _fail);
                    _lastError = ex.GetType().Name;
                    health.Push($"scheduling:task:{Id}", Koan.Core.Observability.Health.HealthStatus.Unhealthy, message: ex.Message, ttl: TimeSpan.FromMinutes(5), facts: Facts("error"));

                    _ = orchestrator.EmitEvent(Koan.Core.Events.KoanServiceEvents.Scheduling.TaskFailed, new TaskFailedEventArgs
                    {
                        TaskId = Id,
                        Error = ex.Message,
                        FailedAt = DateTimeOffset.UtcNow,
                        Exception = ex
                    });
                }
                finally
                {
                    // Recompute the next recurring run from "now" so cron and fixed-delay both advance.
                    NextRunUtc = ComputeNextRun(DateTimeOffset.UtcNow);
                }
            }
            finally
            {
                cts?.Dispose();
                Interlocked.Decrement(ref _running);
            }
        }

        private IReadOnlyDictionary<string, string> Facts(string state)
        {
            var dict = new Dictionary<string, string>
            {
                ["id"] = Id,
                ["state"] = state,
                ["critical"] = critical ? "true" : "false",
                ["running"] = _running.ToString(),
                ["success"] = _success.ToString(),
                ["fail"] = _fail.ToString(),
            };
            if (_skippedWindow > 0) dict["skippedWindow"] = _skippedWindow.ToString();
            if (_lastError is not null) dict["lastError"] = _lastError;

            // IHealthFacts: surface the task's own facts through the same health snapshot. Namespaced
            // with a "task." prefix so a task can never clobber the orchestrator's own keys.
            if (healthFacts is not null)
            {
                try
                {
                    var custom = healthFacts.GetFacts();
                    if (custom is not null)
                    {
                        foreach (var kvp in custom)
                            dict[$"task.{kvp.Key}"] = kvp.Value;
                    }
                }
                catch
                {
                    // A misbehaving GetFacts() must never break the health push; record it as a fact.
                    dict["task.factsError"] = "true";
                }
            }
            return dict;
        }
    }
}
