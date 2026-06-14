---
id: AGYO-0001
title: Agyo Tools charter and the Koan capability reorganization
status: Accepted
date: 2026-06-14
---

# AGYO-0001 — Agyo Tools charter and the Koan capability reorganization

## Status

Accepted (2026-06-14).

## Context

A maturity assessment of the Koan framework produced a stash of *removal* prompts — capabilities to cut because they are not part of the core's identity (**data · web · cache · jobs · mcp · auth · storage**). Executing those cuts surfaced a recurring third case: a capability that is **not core**, yet is **well-engineered and/or actively consumed**, so cutting it *loses* real value. The cut-or-keep binary could not express it.

An independent per-capability re-evaluation of 11 removal targets (see the Koan repo's `docs/assessment/08-agyo-reorganization.md`) found **zero** were core, but **nine** carried preservable capability. That is the gap this repo fills.

## Decision

**Agyo Tools is the home for opt-in, peripheral, app-building helpers for Koan** — "PowerToys for Koan". It is governed by these rules:

1. **Layering (binding, STACK-0001).** Agyo depends on Koan's **public packages** (`Sylin.Koan.*` via `PackageReference`) and is **never referenced by Koan**. Names never flow down. Every capability admitted here must touch only Koan's public surface — that extractability is the entry criterion.
2. **Shape parity.** Agyo follows Koan's repo canon: SDK-style projects, NBGV versioning, `Sylin.$(MSBuildProjectName)` package IDs (→ `Sylin.Agyo.*`), SourceLink, the surface-ledger rotation contract, Apache-2.0 + DCO. The skeleton is ported from Koan, not reinvented.
3. **Rebrand on move.** Migrated code is renamed `Koan.*` → `Agyo.*` (namespaces, assemblies, package IDs). This is a one-time breaking change for downstream consumers, who re-point from `Sylin.Koan.<X>` to `Sylin.Agyo.<X>`.
4. **Integration-tests-as-canon.** A capability is not "done" here until it ships at least one real integration spec (ARCH-0079 discipline). Several migrants (Rag, PGVector, GraphQl) arrive untested — that debt is paid on arrival, not deferred.

## The classification

| → Agyo (move) | → split | → delete (stays in Koan's history) |
|---|---|---|
| Tagging · Secrets · Scheduling · Rag · GraphQl · WebSockets · PGVector | Recipe (delete the bootstrap idiom, **move** the Observability bundle as a `KoanModule`) · ServiceMesh (delete the mesh, **move** Translation decoupled) | Cqrs · Inbox-Redis · ServiceMesh · Strict-STJ · AI Pipelines · Media output-cache · Storage ResilientDecorator |

## Migration order and transition safety

1. **Found** this repo to Koan's shape (done — this commit).
2. **Resurrect** the cut-but-valuable from Koan git refs/attic (WebSockets, Rag, GraphQl, PGVector, Translation) — nothing in Koan changes.
3. **Move** the consumer-facing earmarked (Tagging, Secrets, Scheduling, Observability) under **transition safety**: stand up Agyo packages green *before* removing anything from Koan; keep Koan publishing the old packages until consumers re-point; only then strip from `Koan.sln` and sweep the doc ledgers.
4. **Finish** the incomplete (PGVector compile, decide Scheduling's cron vaporware, optionally OTel).

## Consequences

- Koan's core gets leaner without capability loss; the framework's release train sheds the maintenance-heavy peripherals (e.g. the HotChocolate CVE cadence rides GraphQl here).
- Downstream consumers take a one-time, well-signposted re-point. The Secrets reflection seam in `Koan.Data.Core` stays in Koan untouched — it resolves Agyo's package when present and no-ops when absent.
- Agyo carries its own surface ledger and green ratchet; it is a peer repo, not a Koan subfolder.
