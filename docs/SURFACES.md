# Surface Ledger

> **Rotation contract.** Before a lane leaves a surface: tag; CI green; a tripwire exists
> for every surface the departing work was exercising; status endpoints tell the truth;
> this ledger is updated. Leave a guard at the door when you leave the room.

Agyo Tools mirrors Koan's surface-ledger discipline. Honesty rule: an unknown exercise
status is written `unknown since <date>`, never a guessed "works". Rows below are seeded
as the migration plan (docs/decisions/AGYO-0001) lands each capability from the Koan repo;
`Last exercised` becomes a real date once a capability ships with a green integration spec.

| Surface | Exercised by | Last exercised | Guard | Notes |
|---|---|---|---|---|
| Repo shape (Directory.Build.props, NBGV, Sylin.Agyo.* package IDs) | full Agyo.sln build + pack + test | 2026-06-14 | scripts/green-ratchet.ps1 | All 13 projects pack as Sylin.Agyo.*; full Agyo.sln test green (22 passed, 0 failed, 4 infra-gated skips). |
| Surface-ledger tripwire | .github/workflows/surfaces.yml | 2026-06-14 | scripts/lint-surfaces.sh | This ledger; runs on push/PR to main+dev. |
| Agyo.WebSockets | Agyo.WebSockets.Tests (ARCH-0079) | 2026-06-14 | 3 specs green | Boot-smoke resolves IWebSocketStreamFactory via real AddKoan() + a TestServer duplex echo round-trip. |
| Agyo.Rag | Agyo.Rag.Tests (ARCH-0079) | 2026-06-14 | 2 green + 1 Ollama-skip | Boot-smoke resolves IRagService via AddKoan(). **Finding: KoanRagAutoRegistrar misses inherited static Entity.Events (needs BindingFlags.FlattenHierarchy) — breaks the [RagCorpus] auto-ingest path.** |
| Agyo.Web.GraphQl | Agyo.GraphQl.Tests (ARCH-0079) | 2026-06-14 | 2 specs green | Boot-smoke (discovery proven by removing the explicit AddKoanGraphQl) + a TestServer GraphQL query round-trip. **Findings: process-global `_registered` static (unsafe multi-container); totalCount (int)(long) cast throws.** |
| Agyo.Data.Vector.PGVector | Agyo.PGVector.Tests (ARCH-0079) | 2026-06-14 | 4 green + 2 pgvector-skip | Boot-smoke registers the pgvector adapter via AddKoan(); upsert+KNN behavioral skips clean without AGYO_PGVECTOR_CONNECTION_STRING. |
| Agyo.Translation | Agyo.Translation.Tests (ARCH-0079) | 2026-06-14 | 2 green + 1 Ollama-skip | Boot-smoke resolves TranslationService via AddKoan() + GetLanguages; translate behavioral gated on AGYO_OLLAMA_ENDPOINT. **Finding: options.Text CS8602 null-deref — wants a guard.** |
| Agyo.Tagging | Agyo.Tagging.Tests (ARCH-0079) | 2026-06-14 | 2 specs green | Tag entity + a TagSet (public/private scopes) round-trip through AddKoan()+InMemory, incl. a negative scope-leak assertion. Koan-side removal deferred (transition safety). |
| Agyo.Secrets | Agyo.Secrets.Tests (ARCH-0079) | 2026-06-14 | 3 specs green | Boot-smoke resolves ISecretResolver via AddKoan(); config-secret resolve + ${secret://} expansion + ToString() masking. The Koan.Data.Core reflection seam stays in Koan. |
| Agyo.Scheduling | Agyo.Scheduling.Tests (ARCH-0079) | 2026-06-14 | 2 specs green | Boot-smoke finds the SchedulingOrchestrator hosted service via AddKoan(); a real IScheduledTask runs end-to-end. finish-vs-document cron decision open; Koan.Web de-bloat is Koan-side. |
| Agyo.Observability | Agyo.Observability.Tests (ARCH-0079) | 2026-06-14 | 2 specs green | KoanModule Register() runs via AddKoan(): HealthCheckService + the "agyo-observability" resilient HttpClient resolve. OTel a TODO stub. (Boot-order-sensitive without the registry generator — test force-loads.) |
