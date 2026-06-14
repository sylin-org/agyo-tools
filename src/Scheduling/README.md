# Sylin.Agyo.Scheduling

Lightweight in-process scheduling primitives for Koan-built applications.

- Target framework: net10.0
- License: Apache-2.0

This is the deliberately-minimal in-proc alternative to the durable `Sylin.Koan.Jobs`
ledger: no queues, no data store, just a 1s poll loop hosting `IScheduledTask` runners.
For durable, cron, or distributed-lock scheduling, use `Sylin.Koan.Jobs` (the
ledger-backed tier).

## Capabilities
- In-process scheduler hosted as a BackgroundService.
- Triggers: `IOnStartup`, `IFixedDelay` (cron reserved for Phase 2).
- Per-job policy via options, attribute hints, and task interfaces.
- Bounded concurrency, per-run timeout, health updates via Koan.Core.

## Install

```powershell
dotnet add package Sylin.Agyo.Scheduling
```

## Minimal setup

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register the scheduler via Koan.Core auto-registrar
// (KoanAutoRegistrar wires SchedulingOptions and the HostedService)
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
```

appsettings.json

```json
{
    "Koan": {
        "Scheduling": {
            "Enabled": true,
            "Jobs": {
                "cleanup": {
                    "OnStartup": true,
                    "FixedDelay": "00:00:10",
                    "Timeout": "00:00:05",
                    "MaxConcurrency": 1
                }
            }
        }
    }
}
```

## Authoring tasks

```csharp
public sealed class CleanupTask : IScheduledTask, IFixedDelay
{
        public string Id => "cleanup";
        public TimeSpan Delay => TimeSpan.FromSeconds(10);
        public Task Run(CancellationToken ct) { /* work */ return Task.CompletedTask; }
}
```

Hints
- Use `IHasTimeout` for bounded work; `IHasMaxConcurrency` to allow parallel runs.
- Mark critical tasks with `IIsCritical` or `[Scheduled(Critical = true)]` to influence health/runbooks.
- Prefer idempotent work; handle cancellations.

> **Honored triggers/policies today**: `IOnStartup`, `IFixedDelay`, `IHasTimeout`,
> `IHasMaxConcurrency` (plus the `[Scheduled]` attribute and `Koan:Scheduling` config
> overrides). The `ICronScheduled` / `IProvidesLock` / `IAllowedWindows` / `IHealthFacts`
> contracts are declared but not yet honored by the orchestrator — that durable / cron /
> distributed-lock story lives in `Sylin.Koan.Jobs`.

## References
- Technical reference: `./TECHNICAL.md`
