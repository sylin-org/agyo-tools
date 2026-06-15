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
| Repo shape (Directory.Build.props, NBGV, Sylin.Agyo.* package IDs) | full Agyo.sln build + pack + test | 2026-06-14 | scripts/green-ratchet.ps1 | All 13 projects pack as Sylin.Agyo.*; full Agyo.sln test green (35 passed, 0 failed, 4 infra-gated skips). Discovery via the Koan registry generator (repo-wide). |
| Surface-ledger tripwire | .github/workflows/surfaces.yml | 2026-06-14 | scripts/lint-surfaces.sh | This ledger; runs on push/PR to main+dev. |
| Agyo.WebSockets | Agyo.WebSockets.Tests (ARCH-0079) | 2026-06-14 | 3 specs green | Boot-smoke resolves IWebSocketStreamFactory via real AddKoan() + a TestServer duplex echo round-trip. |
| Agyo.Rag | Agyo.Rag.Tests (ARCH-0079) | 2026-06-14 | 3 green + 1 Ollama-skip | Boot-smoke resolves IRagService via AddKoan() + a [RagCorpus]-decorated entity boots cleanly. FIXED: KoanRagAutoRegistrar now uses BindingFlags.FlattenHierarchy (the [RagCorpus] auto-ingest path works). |
| Agyo.Web.GraphQl | Agyo.GraphQl.Tests (ARCH-0079) | 2026-06-14 | 5 specs green | Boot-smoke + TestServer query + totalCount + multi-container safety. FIXED: per-IServiceCollection sentinel (was a process-global `_registered` static); totalCount uses Convert.ToInt32 (was an unbox cast that threw). |
| Agyo.Data.Vector.PGVector | Agyo.PGVector.Tests (ARCH-0079) | 2026-06-14 | 4 green + 2 pgvector-skip | Boot-smoke registers the pgvector adapter via AddKoan(); upsert+KNN behavioral skips clean without AGYO_PGVECTOR_CONNECTION_STRING. |
| Agyo.Translation | Agyo.Translation.Tests (ARCH-0079) | 2026-06-14 | 7 green + 1 Ollama-skip | Boot-smoke + GetLanguages + a null/empty/whitespace input guard; translate behavioral gated on AGYO_OLLAMA_ENDPOINT. FIXED: fail-fast ArgumentException guard (was an options.Text null-deref). |
| Agyo.Tagging | Agyo.Tagging.Tests (ARCH-0079) | 2026-06-14 | 2 specs green | Tag entity + a TagSet (public/private scopes) round-trip through AddKoan()+InMemory, incl. a negative scope-leak assertion. Koan-side removal deferred (transition safety). |
| Agyo.Secrets | Agyo.Secrets.Tests (ARCH-0079) | 2026-06-14 | 3 specs green | Boot-smoke resolves ISecretResolver via AddKoan(); config-secret resolve + ${secret://} expansion + ToString() masking. The Koan.Data.Core reflection seam stays in Koan. |
| Agyo.Scheduling | Agyo.Scheduling.Tests (ARCH-0079) | 2026-06-14 | 5 specs green | FULLY IMPLEMENTED: cron (Cronos), allowed-windows, in-proc provides-lock, health-facts — each with a spec (plus OnStartup/FixedDelay). Fixed a double-fire registration bug. Koan.Web de-bloat is Koan-side. |
| Agyo.Observability | Agyo.Observability.Tests (ARCH-0079) | 2026-06-14 | 3 specs green | KoanModule via AddKoan(): HealthCheckService + the "agyo-observability" resilient HttpClient + REAL OTel (Tracer/MeterProvider resolve). OTel fully implemented (tracing+metrics+OTLP, safe without a collector). Discovery now via the registry generator (repo-wide). |
| Agyo.Service.Librarian | Agyo.Service.Librarian.Tests (ARCH-0079) | 2026-06-15 | 3 specs green + 1 infra-gated behavioral | Re-homed from Koan's Koan.Service.KoanContext (AGYO-0002); the first Agyo.Service.* runnable tool. Boot-smoke composes the WHOLE service via real AddKoan() (search/indexing pipeline + IndexProjectAsync delegate + file-watch hosted services + JobMaintenanceTask re-homed onto Agyo.Scheduling). MCP: Project read-only [McpEntity] + the five Context7 [McpTool] verbs (real /mcp transport) asserted via the registry. Behavioral spec verified the ingest pipeline LIVE (Ollama all-minilm + per-project Weaviate class provisioning, 0 errors; fixed a force-reindex manifest bug). The search round-trip rides the bespoke async outbox and is pending the P4c Agyo.Rag re-platform — see AGYO-0002 §"Verified vs pending". |
