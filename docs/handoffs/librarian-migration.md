# Handoff: KoanContext → Agyo.Service.Librarian

> **Status:** ready to execute · **Created:** 2026-06-14 · **Owner:** new session (independent of the assessment/prompts effort)
>
> This is a self-contained migration brief. Paste the activation prompt (bottom of this doc, or the one
> handed to you) into a fresh session; it points here. Work only what this brief scopes.

## Mission

Rename → re-home → re-platform → un-break the `KoanContext` service into agyo-tools as
**`Agyo.Service.Librarian`**. It is an independent product on its own lifecycle — do **NOT** touch
`docs/assessment/prompts/**` or `src/Koan.Mcp/**` (concurrent work in both repos).

## Two repos (both on branch `dev`, both pushed)

- Koan framework: `F:\Replica\NAS\Files\repo\github\sylin-org\koan-framework`
- agyo-tools: `F:\Replica\NAS\Files\repo\github\sylin-org\agyo-tools` (the "PowerToys for Koan" sibling repo)

agyo-tools was recently founded to Koan's exact repo canon and now hosts 9 capabilities migrated out of
Koan as `Sylin.Agyo.*` packages. **Read these first — they define the conventions you must follow:**

- `agyo-tools/docs/decisions/AGYO-0001-charter-and-reorganization.md` — the charter: what agyo is, the layering law.
- `agyo-tools/docs/decisions/STACK-0001-sylin-stack-canon.md` — layering: agyo depends on Koan's **public packages**, never the reverse; names never flow down.
- `agyo-tools/Directory.Build.props` — package-id convention `Sylin.$(MSBuildProjectName)`, NBGV, SourceLink, the registry generator.
- `agyo-tools/nuget.config` + `agyo-tools/local-feed/` — how agyo resolves CURRENT Koan bits (see "local feed" below).
- `agyo-tools/docs/SURFACES.md` — the surface-ledger rotation contract (you will add a row).
- `agyo-tools/tests/Agyo.Testing/Integration/AgyoIntegrationHost.cs` (+ `Infrastructure/InfraProbe.cs`) — the ARCH-0079 test harness + skip-clean infra gating.
- `agyo-tools/src/Tagging/` and `agyo-tools/src/Translation/` + their tests — reference examples of a finished migration.

### The local feed (critical mechanism)

agyo references Koan's CURRENT source (public nuget.org Koan is stale at 0.8.x; live is 0.17.x). Koan is
packed into `agyo-tools/local-feed/` (git-ignored), and `agyo-tools/nuget.config` maps `Sylin.Koan.*` to
that feed. To (re)populate or add packages:
`dotnet pack <koan>\Koan.sln -c Release -o <agyo>\local-feed` (or pack individual csprojs). **All 10 Koan
packages this product needs are ALREADY in the feed:** `Sylin.Koan.{Core, Data.Core,
Data.Connector.Sqlite, Web, Web.Sse, Web.Connector.Swagger, Mcp, AI, Data.Vector,
Data.Vector.Connector.Weaviate, AI.Connector.Ollama, Orchestration.Aspire}`. Re-pack if you bump anything.

## What we have — the product

`Koan.Service.KoanContext` at
`F:\Replica\NAS\Files\repo\github\sylin-org\koan-framework\src\Services\code-intelligence\Koan.Service.KoanContext`
is a **self-hosted, local-first "Context7"** — a code/docs intelligence service. You register a repo; it
indexes that repo's code+docs into a per-project Weaviate vector store (one class per project via
EntityContext partitions) and serves grounded, cited chunks to AI coding agents over **MCP**, plus a **Web
UI** and **REST API**. Spec + intent:

- `<koan>\docs\archive\proposals\Koan-context.md` — the spec ("local-first Context7-style service").
- `<koan>\docs\archive\proposals\Koan-context-checklist.md` — milestones.
- `<koan>\docs\archive\proposals\koan-context-ai-optimization-phase1.md` — a later retrieval-tuning pass.

Shape (67 C# files ~14.8k LOC + a 51-file TypeScript UI under `ui/` + `wwwroot/`):

- ASP.NET host (`Microsoft.NET.Sdk.Web`, `OutputType=Exe`, `RootNamespace`/`AssemblyName=Koan.Context`).
- Pipeline (`Services/`): Discovery → Extraction → Chunker → Embedding (Ollama) → Indexer → Search
  (hybrid vector+BM25), plus IncrementalIndexing, FileMonitoring, IndexingCoordinator/Planner/
  Resumption{Queue,Worker}, VectorSyncWorker, ProjectResolver, TagResolver, Metrics.
- MCP tools (Context7-compatible verbs): `context.resolve_library_id`, `context.get_library_docs`,
  `context.list_projects`, `context.project_status`, `context.reindex_project`.
- Controllers: Projects, Search, Jobs, Tags, TagRules, TagPipelines, SearchPersonas, Settings, Metrics,
  Diagnostics, Streaming(SSE), McpTools.
- Models: Project, Chunk, ChunkVectorMetadata/State, IndexedFile, IndexingPlan, Job, SearchPersona/Category,
  SyncOperation.
- Storage: local `.koan/data/Koan.sqlite` (project/job metadata) + Weaviate (vectors), Aspire-orchestrated.

### Current STATE — important

It is **out of `Koan.sln`, mid-refactor, and does NOT compile.** Known breakage:

- (a) An `IndexProject`→`IndexProjectAsync` rename left half-done — `IndexProject` exists as a delegate
  (`Services/IndexingDelegates.cs`), a method (`Services/Indexer.cs:113`), and a controller action
  (`Controllers/ProjectsController.cs:119`), with a non-awaited call in `Program.cs:58`. Resolve the
  collision (consistent `...Async` naming + await at call sites).
- (b) It still has a `ProjectReference` to `..\..\..\Koan.Scheduling\Koan.Scheduling.csproj` — but
  **Koan.Scheduling was removed from Koan** (migrated to agyo as `Sylin.Agyo.Scheduling`); that ref is
  dangling. So it cannot build in-place at all.
- (c) It has been out-of-sln long enough that other current-Koan API drift is likely; expect to fix it.

Its bespoke retrieval pipeline **predates and substantially duplicates** the RAG toolkit that now lives in
agyo as `Sylin.Agyo.Rag` (public surface: `Rag.Corpus<TEntity>()` → `IRagCorpus<TEntity>` with
ingest/query, `Rag.IsAvailable`, `IRagService`). A librarian should use the library.

## What we'll be doing (the decision — record it as AGYO-0002)

1. **Re-home to agyo** as a runnable service named **`Agyo.Service.Librarian`** (NOT a `Sylin.Agyo.*`
   package — it's an app you run, not a library you reference). Use the `Agyo.Service.*` convention: it
   cleanly distinguishes agyo's runnable TOOLS from its opt-in LIBRARIES (`Sylin.Agyo.*`). Place it under
   `agyo-tools/src/Service/Librarian/` (or `services/Librarian/` — match agyo's layout sensibly). Rebrand
   `Koan.Context`/`Koan.Service.KoanContext` → `Agyo.Service.Librarian` (namespaces, assembly,
   RootNamespace, config sections `Koan:Context:*`→`Agyo:Librarian:*`, UI titles/branding). Bring `ui/` +
   `wwwroot/` along. Add it to `Agyo.sln`. It is NOT packable (`IsPackable=false`, `OutputType=Exe`).
2. **Re-point all dependencies to packages.** Convert the 13 Koan `ProjectReference`s to
   `PackageReference Sylin.Koan.*` (versions from local-feed), the dangling Scheduling ref to
   `PackageReference Sylin.Agyo.Scheduling` (or drop it if re-platforming removes the need), keep the two
   third-party refs (AspNetCoreRateLimit, Polly). Layering law: only `Sylin.Koan.*`/`Sylin.Agyo.*`
   references, never a path back into the Koan repo.
3. **Un-break it** — get `Agyo.Service.Librarian` building green against current Koan packages (fix the
   IndexProject collision + any current-Koan API drift).
4. **Re-platform onto `Sylin.Agyo.Rag`** — replace the bespoke Discovery/Chunker/Embedding/Indexer/Search
   pipeline with `Rag.Corpus<T>()` ingest+retrieval, KEEPING the Librarian-specific surface that Rag does
   NOT provide: the MCP tool contract, the Web UI, the REST controllers, the jobs/file-watch incremental
   re-indexing, and the Tags/TagRules/TagPipelines/SearchPersonas layer. This is the real engineering work
   — scope it pragmatically (see "decisions"); a partial re-platform that deletes the clearest duplication
   is acceptable for a first pass if a full one is too large, as long as you say so.

## Decisions to make (recommendations — confirm or override with the human)

- **MCP tool names:** keep the **Context7-compatible verbs** (`resolve_library_id` / `get_library_docs`)
  so an agent already configured for Context7 is drop-in — recommended. Product/package name is Librarian;
  the tool *verbs* stay compatible (optionally under a `librarian.*` prefix). Don't rename the verbs
  without a reason.
- **Re-platform depth:** full (Librarian becomes a thin Web/MCP/UI shell over `Agyo.Rag`) vs partial (swap
  the embed+vector+search core to Rag, keep Librarian's discovery/tagging). Recommend partial-now,
  full-later, and say which you did.
- **The UI:** bring it as-is (rebrand strings only) now; defer any redesign.

## What to deliver (acceptance)

- `Agyo.Service.Librarian` builds green in `Agyo.sln`; `dotnet build Agyo.sln` and the green-ratchet pass.
- It RUNS: it boots via `AddKoan()`, the MCP endpoint lists the tools, the REST API + Web UI serve. Show
  evidence (a boot smoke + at least a `resolve_library_id`/`get_library_docs` round-trip on a tiny indexed
  sample; gate the Weaviate/Ollama-dependent parts skip-clean via `InfraProbe`).
- At least one **ARCH-0079 integration test** through real `AddKoan()` using `AgyoIntegrationHost`
  (boot-smoke that the MCP tools + primary services resolve; behavioral indexing/retrieval test gated on
  infra). Tests live under `agyo-tools/tests/`, follow the existing harness + AwesomeAssertions/xUnit shape.
- **`agyo-tools/docs/decisions/AGYO-0002-librarian.md`** — records the rename/re-home/re-platform decision,
  the Context7 lineage, the `Agyo.Service.*` convention, and the re-platform scope you landed.
- A `docs/SURFACES.md` row + a project README (what it is, how to run it, the MCP tool contract).
- **Koan-side:** once Librarian lives in agyo, `git rm -r` the old
  `src/Services/code-intelligence/Koan.Service.KoanContext` from Koan and close the **X-KoanContext** row
  in `docs/assessment/prompts/PROGRESS.md` (note: re-homed to agyo as Agyo.Service.Librarian). Confirm
  `dotnet build Koan.sln` stays green afterward (it's out-of-sln, so this is just tree cleanup).
- Commit in coherent steps with conventional-commit messages; push both repos' `dev`.

## Guardrails

- Layering (STACK-0001): agyo → `Sylin.Koan.*`/`Sylin.Agyo.*` packages only; never reference back into Koan.
- Do NOT touch: `docs/assessment/prompts/**` (separate effort), `src/Koan.Mcp/**` or
  `tests/Suites/Samples/Koan.Mcp.*` (concurrent work) in either repo.
- Persona separation: never write the name of any downstream consumer repo into either tree — genericize
  as "downstream consumer" if it ever comes up.
- Verify empirically — build and run, don't reason-and-assert. No stopgaps; fix root causes (don't fake a
  capability to a floor). If you find a real bug, fix it properly or report it; don't patch tests to green.
- Re-pack the local feed if you need a Koan package that isn't current in it.

## Suggested first moves

1. Read the AGYO-0001 / STACK-0001 / Directory.Build.props / nuget.config + the Tagging+Translation
   reference migrations, and the KoanContext spec.
2. Move + rebrand the project into agyo, re-point refs to packages, and get a (failing) restore so you can
   see the real compile surface.
3. Un-break to green, THEN re-platform onto `Agyo.Rag`. Report your plan + the re-platform scope before
   large changes.
