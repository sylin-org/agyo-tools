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
| Repo shape (Directory.Build.props, NBGV, Sylin.Agyo.* package IDs) | local build + pack | 2026-06-14 | scripts/green-ratchet.ps1 (build leg) | Validated: Sylin.Agyo.WebSockets.0.1.2 packs with correct id/version/license/Koan dependency. |
| Surface-ledger tripwire | .github/workflows/surfaces.yml | 2026-06-14 | scripts/lint-surfaces.sh | This ledger; runs on push/PR to main+dev. |
| Agyo.WebSockets | local build + pack | 2026-06-14 | scripts/green-ratchet.ps1 (build leg) | Migrated from Koan ffef0899~1; builds+packs against Sylin.Koan.Core. Unit tests (5) still to port — not test-canon "done" per AGYO-0001. |
| Agyo.Rag | local build + pack | 2026-06-14 | green-ratchet build leg | Migrated from Koan attic/Koan.Rag (2 projects); Sylin.Agyo.Rag packs, layering clean. Ships untested — integration spec pending (AGYO-0001). |
| Agyo.Web.GraphQl | local build + pack | 2026-06-14 | green-ratchet build leg | Migrated from Koan tag attic/koan-web-graphql; Sylin.Agyo.Web.GraphQl packs (HotChocolate 13.9.16). Integration spec pending. |
| Agyo.Data.Vector.PGVector | local build + pack | 2026-06-14 | green-ratchet build leg | Migrated from Koan tag attic/pgvector; IVectorFilterTranslator finish done, Sylin.Agyo.Data.Vector.PGVector packs. Real pgvector integration spec pending (container-gated). |
| Agyo.Translation | local build + pack | 2026-06-14 | green-ratchet build leg | Migrated from Koan f7d8a499~1, decoupled from the deleted ServiceMesh; Sylin.Agyo.Translation packs. Integration spec pending (Ollama-gated). |
| Agyo.Tagging | local build + pack | 2026-06-14 | green-ratchet build leg | Migrated from Koan src/Koan.Tagging (clean lift, doc-drift cleaned); Sylin.Agyo.Tagging packs. Integration spec pending. Koan-side removal deferred (transition safety). |
| Agyo.Secrets | none yet | unknown since 2026-06-14 | pending migration | Earmarked from Koan src/Koan.Secrets.*; the Koan.Data.Core reflection seam stays in Koan. |
| Agyo.Scheduling | none yet | unknown since 2026-06-14 | pending migration | Earmarked from Koan src/Koan.Scheduling; finish-or-document the cron/locks vaporware. |
| Agyo.Observability | none yet | unknown since 2026-06-14 | pending migration | Earmarked from Koan Recipe.Observability bundle, re-expressed as a KoanModule. |
