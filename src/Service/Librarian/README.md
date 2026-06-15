# Agyo.Service.Librarian

A self-hosted, local-first **"Context7" for your own repos** — a code/docs intelligence service. You
register a repository; Librarian indexes its code + docs into a per-project vector store and serves
grounded, cited chunks to AI coding agents over an MCP tool, a REST API, and a web UI.

It is an **app you run**, not a library you reference (`OutputType=Exe`, `IsPackable=false`). It is the
first `Agyo.Service.*` tool — re-homed from Koan's `Koan.Service.KoanContext`. See
[AGYO-0002](../../../docs/decisions/AGYO-0002-librarian.md) for the rename/re-home/re-platform decision.

> **Lineage:** Librarian is a local-first take on the Context7 idea (resolve a library, fetch grounded
> docs). The Context7-compatible verbs (`resolve_library_id`, `get_library_docs`, `list_projects`,
> `project_status`, `reindex_project`) are exposed as **real Koan.Mcp tools** so an agent already
> configured for Context7 is a drop-in. (Delivering this required extending Koan.Mcp with a custom-verb
> `[McpTool]` capability — see AGYO-0002.)

## What it does

Pipeline: **Discovery → Extraction → Chunker → Embedding (Ollama) → Indexer → Search** (hybrid
vector + BM25), with a Tags/TagRules/TagPipelines vocabulary layer, SearchPersonas (weighting profiles),
git-commit + line provenance, per-project partition isolation, indexing Jobs, and a FileSystemWatcher-
driven incremental re-index. Storage: local SQLite for metadata, a Weaviate class per project for vectors,
Aspire-orchestrated.

## Run it

```bash
dotnet run --project src/Service/Librarian/Agyo.Service.Librarian.csproj
```

Defaults to `http://localhost:27500` (localhost-only). On boot, `AddKoan()` auto-provisions a Weaviate
container (host port 27501) via Aspire when a container runtime is present; point `Koan:Data:Weaviate:
Endpoint` at an existing Weaviate to use your own. An Ollama endpoint (default `localhost:11434`, model
`all-minilm`) provides embeddings.

- **MCP** (real `/mcp` transport): the Context7-compatible custom tools `list_projects`,
  `resolve_library_id`, `project_status`, `reindex_project`, `get_library_docs` (`Mcp/ContextTools.cs`),
  plus the read-only `project.*` entity tools (`Project` `[McpEntity]`). The legacy
  `POST /api/mcp/get-references` REST endpoint is also preserved. (`get_library_docs` returns cited chunks
  once the P4c re-platform closes the vector-write gap.)
- **REST**: `/api/projects`, `/api/search`, `/api/jobs`, `/api/tags`, `/api/tag-rules`,
  `/api/tag-pipelines`, `/api/search-personas`, `/api/settings`, `/api/metrics`, `/api/diagnostics`,
  `/api/stream` (SSE).
- **Web UI**: the React SPA served from `wwwroot/`.

## Configuration

App-owned settings live under `Agyo:Librarian:*` (security, indexing performance, file monitoring, project
resolution, job maintenance). Framework settings keep their `Koan:*` root: `Koan:Data` (SQLite + Weaviate),
`Koan:Ai` (Ollama), `Koan:Orchestration:Weaviate`, `Koan:Mcp`. See `appsettings.json`.

## Tests

ARCH-0079 specs live in `tests/Service/Librarian/Agyo.Service.Librarian.Tests`:

- **Boot-smoke** (always runs): a real `AddKoan()` host composes the full service surface.
- **Behavioral** (skip-clean): set `AGYO_WEAVIATE_ENDPOINT` + `AGYO_OLLAMA_ENDPOINT` (with `all-minilm`
  pulled) to run the live ingest pipeline. The retrieval round-trip is logged, not asserted, pending the
  P4 Agyo.Rag re-platform (AGYO-0002 §"Verified vs pending").

## Status

Green re-home complete (builds, boots, composes, indexes against live infra). The bespoke retrieval
pipeline is slated to be slimmed onto an enriched `Agyo.Rag` (AGYO-0002 §5).
