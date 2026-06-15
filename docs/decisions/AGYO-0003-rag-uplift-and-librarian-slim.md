---
id: AGYO-0003
title: Rag uplift + Librarian slim — harvest the KoanContext ideas into Agyo.Rag, then re-platform Librarian onto it
status: Accepted
date: 2026-06-15
---

# AGYO-0003 — Rag uplift + Librarian slim

## Status

Accepted (2026-06-15). The uplifts landed in `Sylin.Agyo.Rag`; `Agyo.Service.Librarian` is re-platformed onto
them; the two Koan-side fixes that the round-trip depended on are committed upstream. The cited index→search
round-trip is verified live against real Ollama + Weaviate. Continues AGYO-0002 §5.

## Context

AGYO-0002 re-homed the KoanContext code-intelligence service into agyo as `Agyo.Service.Librarian` and
decided (§5) that the re-platform onto `Sylin.Agyo.Rag` would be a **harvest, not a swap**: study both
pipelines, port the agnostic, legitimately-good KoanContext ideas *up* into the shared `Agyo.Rag` library,
then **slim** Librarian to consume the enriched Rag — without regressing citation precision.

The two pipelines differ in shape:

- **Agyo.Rag** was **entity-first**: a corpus binds to an `Entity<T>`; retrieval hydrated the *whole* source
  entity, and `RagChunk` carried no line/url/commit provenance.
- **KoanContext/Librarian** is **chunk-first, cite-precisely**: it indexes file chunks and answers with the
  exact chunk text + file/line provenance.

A comparative study (run as a fan-out workflow) confirmed four agnostic ideas worth lifting into Rag, and that
the Librarian's bespoke retrieval stack (Discovery → Extraction → Chunker → Embedding → Indexer + a
transactional-outbox vector sync) substantially duplicated Rag once those ideas were present.

## Decision

### 1. Harvest four agnostic capabilities up into `Agyo.Rag`

1. **Chunk provenance** — `RagChunkProvenance(FilePath, StartLine, EndLine, Language, CommitSha, SourceUrl)`
   (`Rag.Abstractions`), an optional trailing member on `RagChunk`. The ingestion pipeline writes
   `text` + provenance keys (`source_path`, `language`, …) into the vector metadata; retrieval reads them back
   via `RagChunkProvenance.FromMetadata(...)` (tolerant of long/string/int boxing across adapters).
2. **Gitignore-aware repo discovery** — `Rag.Discover(repoRoot[, RepoDiscoveryOptions])` produces the file
   list for `Corpus<T>().Ingest(...)`. Zero-config defaults cover common code+docs types and exclude
   build/vendor/VCS output; `.gitignore` is honored for simple (non-negated) rules. Dependency-free
   (`Directory.EnumerateFiles` + glob), so it adds no package surface.
3. **Retrieval knobs** — `RagQueryOptions.HybridAlpha`, `RerankTopN`, `MaxContextTokens` (all nullable;
   `null` = the global `RagOptions` default). Per-query overrides with a token-budget trim that keeps the
   highest-scored chunks.
4. **File-watch incremental ingest** — an opt-in helper that watches a repo root and re-ingests changed files
   into a corpus (debounced), so a long-running consumer keeps its index fresh without a bespoke monitor.

The Rag retrieval pipeline now prefers the **stored chunk text** carried on `VectorMatch.Metadata` and only
falls back to hydrating the source entity for entity-ingested corpora — so file-ingested corpora are
chunk-precise and cite-by-file, while the original entity-first behavior is preserved unchanged.

### 2. Slim `Agyo.Service.Librarian` onto the enriched Rag

The Librarian's ingest + search are re-platformed onto `Rag.Corpus<LibraryDoc>()`:

- **Ingest** (`RagIndexService`) = `Rag.Discover(project.RootPath)` → `Corpus<LibraryDoc>().Ingest(files)` under
  `EntityContext.Partition(projectId)` (one vector class per project), wrapped in the job lifecycle.
- **Search** (`RagSearchService : ISearchService`) = a thin persona/token-budget re-ranker over
  `Corpus<LibraryDoc>().Search(...)`, mapping the provenance-bearing `RagChunk`s back into the Librarian's
  cited `SearchResult` shape (file/line/url/commit).
- The bespoke **Chunker / Embedding / Indexer** and the **transactional-outbox vector sync**
  (`ChunkVectorState` → `VectorSyncWorker`) are **dropped**. Rag writes vectors **inline** at ingest, which is
  what closes the async-outbox persistence gap that left AGYO-0002's search half pending.
- **Kept local** (not generic enough to harvest): the Discovery/Extraction front-half wiring, the
  rule/pipeline/envelope **tag-inference engine** (vocabulary converges on `Sylin.Agyo.Tagging` per AGYO-0002),
  the **persona** ranking, the **MCP/REST/SPA** surface.

`LibraryDoc : Entity<LibraryDoc>` carries `[VectorSchema(typeof(LibraryDocVectorMetadata))]`; the metadata
property names match Rag's ingest keys exactly so the per-project Weaviate class is provisioned correctly and
the chunk text + provenance round-trip through `VectorMatch.Metadata`.

### 3. Two Koan-side fixes the round-trip depended on (committed upstream)

The slim exposed two genuine framework bugs — fixed at root in Koan (authorized under AGYO-0002 P4b's
"expand/refactor Koan.Mcp" + the no-stopgaps rule):

- **`feat(weaviate): return stored object metadata on vector search`** — the Weaviate adapter's search
  requested only `docId` + `_additional`, so `VectorMatch.Metadata` was always `null` (PGVector returns it).
  The search GraphQL now also requests the ensured metadata properties and parses them into `Metadata`, so the
  chunk text + provenance survive the round-trip. Without this, cited search returns a placeholder.
- **`fix(mcp): stdio transport deadlocked host shutdown`** — the MCP stdio transport (default-on whenever an
  `[McpEntity]` is registered) deadlocked host shutdown: its cancellation callback called `rpc.Dispose()`
  synchronously, which blocks on a read loop parked in an uncancellable console-stdin `ReadFile`. Confirmed
  via a test-host hang dump. Fixed by (a) the dispatcher only *signalling* cancellation (no inline Dispose;
  `RunContinuationsAsynchronously` so the disposal continuation never runs on the cancel thread) and
  (b) `StdioTransport.StopAsync` disposing stdin to break the blocking read, with a bounded wait so the host
  can never hang. This affected any Koan host that starts the stdio transport, not just the Librarian.

## What the study corrected

- The re-platform is genuinely a **reduction**: with provenance + discovery + knobs in Rag, the Librarian's
  ingest/search collapse to thin adapters. The bespoke async-outbox was not "fixed in place" (AGYO-0002 §5) —
  it was deleted, because Rag's inline write supersedes it.
- The Weaviate "persistence gap" suspected in AGYO-0002 was **two** distinct issues: the bespoke outbox never
  landed the object (superseded), *and* even a landed object came back without metadata on search (the Koan
  Weaviate fix above). Both had to be closed for the cited round-trip to work.

## Verified vs pending (honesty rule)

**Verified (live, this session):**
- `Agyo.Rag` uplift unit tests green (provenance parse, additive `RagChunk`, knob defaults, discovery).
- Direct Weaviate inspection confirms an ingested chunk stores the real `text` + `source_path` + `language` +
  `section` + `is_child` properties, and the connector's GraphQL search returns them.
- The Librarian **boot-smoke** (ARCH-0079) composes the re-platformed surface from a single `AddKoan()`:
  `RagIndexService`, `ISearchService` = `RagSearchService`, the `IndexProjectAsync` delegate, the re-homed
  `JobMaintenanceTask`, `Project` as a read-only `[McpEntity]`, and the five Context7 `[McpTool]` verbs.
- The **index→search behavioral spec** (live Ollama `all-minilm` + live Weaviate) asserts the cited round-trip:
  ingest runs with 0 errors and search returns the **real** chunk text (not the hydration placeholder) with
  file provenance. The host shuts down cleanly (the stdio-deadlock fix).

**Pending / follow-ups:**
- `LibraryDoc`'s `[VectorSchema(EntityName = "LibrarianDocVector")]` is not reflected in the Weaviate class
  name (the class is named from the full type + partition). Cosmetic; tracked, not blocking.
- The Koan-side fixes are validated via the local feed (`Sylin.Koan.Data.Vector.Connector.Weaviate 0.17.1`,
  `Sylin.Koan.Mcp 0.17.6`); they ride into a public Koan release on the next pack.

## Consequences

- `Sylin.Agyo.Rag` gains provenance, discovery, retrieval knobs, and opt-in file-watch — useful to every Rag
  consumer, not just the Librarian.
- `Agyo.Service.Librarian` sheds its bespoke chunk/embed/index/outbox stack; ingest + cited search are thin
  adapters over Rag with no regression in citation precision.
- Koan gains a real Weaviate search-metadata capability (parity with PGVector) and a fixed MCP stdio shutdown.
