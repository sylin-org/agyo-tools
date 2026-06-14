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
| Repo shape (Directory.Build.props, NBGV, Sylin.Agyo.* package IDs) | local build + pack | unknown since 2026-06-14 | scripts/green-ratchet.ps1 (build leg) | Skeleton ported from Koan canon; pre-first-package. |
| Surface-ledger tripwire | .github/workflows/surfaces.yml | 2026-06-14 | scripts/lint-surfaces.sh | This ledger; runs on push/PR to main+dev. |
| Agyo.WebSockets | none yet | unknown since 2026-06-14 | pending migration | Resurrect from Koan ffef0899~1; bidirectional duplex streaming (distinct from SSE). |
| Agyo.Rag | none yet | unknown since 2026-06-14 | pending migration | Move from Koan attic/Koan.Rag; ships untested today — needs a first spec. |
| Agyo.Web.GraphQl | none yet | unknown since 2026-06-14 | pending migration | Resurrect from Koan tag attic/koan-web-graphql; HotChocolate CVE cadence now lives here. |
| Agyo.Data.Vector.PGVector | none yet | unknown since 2026-06-14 | pending migration | Resurrect from Koan tag attic/pgvector; needs the IVectorFilterTranslator finish + a real pgvector spec. |
| Agyo.Translation | none yet | unknown since 2026-06-14 | pending migration | Resurrect from Koan f7d8a499~1; decouple from the deleted ServiceMesh. |
| Agyo.Tagging | none yet | unknown since 2026-06-14 | pending migration | Earmarked from Koan src/Koan.Tagging; move under transition safety (consumer-facing). |
| Agyo.Secrets | none yet | unknown since 2026-06-14 | pending migration | Earmarked from Koan src/Koan.Secrets.*; the Koan.Data.Core reflection seam stays in Koan. |
| Agyo.Scheduling | none yet | unknown since 2026-06-14 | pending migration | Earmarked from Koan src/Koan.Scheduling; finish-or-document the cron/locks vaporware. |
| Agyo.Observability | none yet | unknown since 2026-06-14 | pending migration | Earmarked from Koan Recipe.Observability bundle, re-expressed as a KoanModule. |
