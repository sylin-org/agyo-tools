# Agyo Tools

**PowerToys for [Koan](https://github.com/sylin-org/koan-framework).** Opt-in helper libraries that an application *reaches for* but the framework doesn't *need* — preserved, maintained, and packaged, instead of cut.

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)

> **Status: pre-1.0 (0.1.x).** Versioned with Nerdbank.GitVersioning. Packages publish as `Sylin.Agyo.*`; code namespaces are `Agyo.*`.

## What this is

Koan's core identity is **data · web · cache · jobs · mcp · auth · storage**. A capability can be genuinely useful, well-engineered, and actively used and still not belong in that core. Before Agyo, the only ways to say "not core" were **cut** (lose it) or **attic** (freeze it). Agyo is the third answer: a sibling repo that **depends on Koan's published packages and is never referenced by Koan** — so the helper lives on its own release cadence without weighing on the framework's.

Each Agyo package is an opt-in `Sylin.Agyo.*` library. "Reference = Intent" still applies: add the package, get the capability auto-wired through Koan's bootstrap.

## The layering law (binding)

Agyo sits **one layer above** Koan (STACK-0001: names never flow down).

```
  Agyo.*   →  references Sylin.Koan.* public packages
  Koan.*   →  has zero knowledge of Agyo
```

Every Agyo project references Koan via **`PackageReference` to `Sylin.Koan.*`**, never a Koan `ProjectReference`. This is enforced by review and is the reason every capability here was extractable in the first place — they each touch only Koan's public surface.

## Capabilities (migrating)

| Package | What | From |
|---|---|---|
| `Sylin.Agyo.WebSockets` | Bidirectional duplex streaming (a `Stream` over a WebSocket) | Koan |
| `Sylin.Agyo.Rag` | Opinionated RAG stack: RAPTOR trees, concept-graph retrieval, GMM clustering, RAGAS-style eval | Koan |
| `Sylin.Agyo.Web.GraphQl` | GraphQL-over-entities, visibility-correct via the shared hook pipeline | Koan |
| `Sylin.Agyo.Data.Vector.PGVector` | Postgres pgvector-backed vector search | Koan |
| `Sylin.Agyo.Translation` | AI translation + language detection (chunking, prompt engineering) | Koan |
| `Sylin.Agyo.Tagging` | Tag classification primitives (TagSet, canonical Tag registry) | Koan |
| `Sylin.Agyo.Secrets` | Secret-reference resolution + provider chain + HashiCorp Vault | Koan |
| `Sylin.Agyo.Scheduling` | Lightweight in-proc scheduler (the minimal alternative to the durable Jobs ledger) | Koan |
| `Sylin.Agyo.Observability` | Health checks + resilient HttpClient (+ OpenTelemetry) bundle, as a `KoanModule` | Koan |

See [docs/decisions/AGYO-0001](docs/decisions/AGYO-0001-charter-and-reorganization.md) for the founding charter and the per-capability migration plan, and [docs/SURFACES.md](docs/SURFACES.md) for the live status of each.

## Develop

Agyo references Koan's **current** bits (the public nuget.org packages lag the framework's live `0.17.x`). Pack Koan locally into the `local-feed/` source this repo's `nuget.config` reads from:

```pwsh
# from the koan-framework repo:
dotnet pack Koan.sln -c Release -o <path-to>/agyo-tools/local-feed

# from this repo:
pwsh scripts/green-ratchet.ps1     # build + test + surface-ledger lint
```

## License

Apache-2.0. See [LICENSE](LICENSE), [NOTICE](NOTICE), and [CONTRIBUTING.md](CONTRIBUTING.md) (DCO sign-off required).
