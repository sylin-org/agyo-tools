// src/Agyo.Service.Librarian/Program.cs
// Console app that exposes Web API, Web UI, and MCP endpoints
// Pattern: follows g1c1.gardencoop with MCP integration

using AspNetCoreRateLimit;
using Agyo.Service.Librarian.Middleware;
using Agyo.Service.Librarian.Infrastructure;
using Agyo.Service.Librarian.Services;
using Agyo.Service.Librarian.Utilities;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Mcp.Extensions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.ConfigureSampleLogging();

// ✅ ONE LINE AUTO-REGISTRATION
// Discovers: entities, controllers, MCP tools, vector adapters, orchestration evaluators
builder.Services.AddKoan();

// Rate limiting (web-host concern; the rate-limit middleware is wired into the pipeline below).
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// NOTE: every Librarian domain/service registration (search, indexing pipeline, file-watch,
// tags/personas, metrics, the IndexProjectAsync delegate, PathValidator, options) is registered by
// KoanAutoRegistrar so the service is fully composed by AddKoan() ("Reference = Intent"). Program.cs
// deliberately keeps only the web-host pipeline below.

// Build app
var app = builder.Build();

// Make services available globally (for static Entity<T> facades)
AppHost.Current ??= app.Services;

// ✅ SECURITY: Global exception handler (must be first in pipeline)
app.UseGlobalExceptionHandler();

// ✅ SECURITY: Rate limiting middleware (must be early in pipeline)
app.UseIpRateLimiting();

// ✅ SECURITY: Security headers (CSP, X-Frame-Options, HSTS, etc.)
app.UseMiddleware<SecurityHeadersMiddleware>();

// ✅ MCP ENDPOINTS (HTTP+SSE transport for Claude Desktop / Cline)
app.MapKoanMcpEndpoints();  // Exposes /mcp/sse, /mcp/rpc, /mcp/health, /mcp/capabilities

// ✅ REST API (CRUD for projects)
app.MapControllers();  // Auto-discovers ProjectsController, etc.

// ✅ WEB UI (React SPA)
app.UseStaticFiles();  // Serves from wwwroot/
app.MapFallbackToFile("index.html");  // SPA routing - React app with deep linking

// Security: bind to localhost only by default
// Port allocation: 27500-27510 range to avoid conflicts
if (!app.Environment.IsDevelopment() && !app.Configuration.GetValue<bool>("AllowExternalAccess"))
{
    app.Urls.Clear();
    app.Urls.Add("http://localhost:27500");
}
else if (app.Urls.Count == 0)
{
    app.Urls.Add("http://localhost:27500");
}

// ✅ VECTOR STORE AUTO-PROVISIONING
// If a vector connector is referenced (e.g., Koan.Data.Vector.Connector.Weaviate):
// - OrchestrationEvaluator auto-registers
// - Aspire detects vector dependency
// - Spins up vector container on first vector operation
// - Endpoint: http://localhost:27501 (mapped from container's default port)
// - Volume: koan-vector-data (persistent)
// - Configuration via appsettings.json or auto-detected

// Configure sample lifecycle with browser launch
app.ConfigureSampleLifecycle(
    sampleName: "Agyo Librarian",
    startupMessage: "Agyo Librarian is listening on {Addresses}. MCP: http://localhost:27500/mcp/sse | API: http://localhost:27500/api/projects",
    shutdownMessage: "Agyo Librarian shutting down.",
    launchBrowser: true);

// Run app
await app.RunAsync();

/*
AUTO-REGISTERED via AddKoan() "Reference = Intent" discovery:
  - Entities: Project, Chunk (+ tag/persona/job entities) as Koan Entity<T>.
  - Controllers: Projects, Search, Jobs, Tags, TagRules, TagPipelines, SearchPersonas,
    Settings, Metrics, Diagnostics, Streaming(SSE), McpTools.
  - Services: the ingest pipeline (Discovery -> Extraction -> Chunker -> Embedding -> Indexer),
    Search, TagResolver, file-watch + incremental indexing, VectorSyncWorker outbox.
  - Vector adapter (Weaviate) + Aspire orchestration evaluator auto-provision the vector store.

MCP surface (current): the get-references tool is served by McpToolsController at
POST /api/mcp/get-references. The Context7-style verbs (resolve_library_id / get_library_docs /
list_projects / project_status / reindex_project) are tracked for AGYO-0002 P4b and are NOT yet
wired into the /mcp transport. Project admin verbs currently live as REST on ProjectsController.
*/
