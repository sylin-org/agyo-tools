using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agyo.Service.Librarian.Models;
using Agyo.Service.Librarian.Services;
using Koan.Data.Core;
using Koan.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Agyo.Service.Librarian.Mcp;

/// <summary>
/// Context7-compatible MCP verbs exposed as real Koan.Mcp custom tools (<c>[McpTool]</c>), so an agent
/// already configured for Context7 is a drop-in over the <c>/mcp</c> transport. These complement the
/// read-only <c>Project</c> <c>[McpEntity]</c> tools (AGYO-0002 P4b). Each verb resolves its services
/// from the injected <see cref="IServiceProvider"/>; <c>Project</c> data is read through the ambient
/// <c>Entity&lt;T&gt;</c> facades.
/// </summary>
public static class ContextTools
{
    [McpTool(Name = "list_projects", Description = "List all repositories registered with the Librarian.")]
    public static async Task<object> ListProjects(IServiceProvider services, CancellationToken ct)
    {
        var all = await Project.All(ct);
        return all.Select(Summarize).ToArray();
    }

    [McpTool(Name = "resolve_library_id",
        Description = "Resolve a library/repo name to candidate Librarian project ids (Context7-compatible).")]
    public static async Task<object> ResolveLibraryId(string libraryName, IServiceProvider services, CancellationToken ct)
    {
        var needle = (libraryName ?? string.Empty).Trim();
        var all = await Project.All(ct);
        var matches = string.IsNullOrEmpty(needle)
            ? all
            : all.Where(p =>
                p.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                p.RootPath.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Id, needle, StringComparison.OrdinalIgnoreCase));
        return matches.Select(Summarize).ToArray();
    }

    [McpTool(Name = "project_status", Description = "Get a Librarian project's indexing status.")]
    public static async Task<object> ProjectStatus(string projectId, IServiceProvider services, CancellationToken ct)
    {
        var project = await Project.Get(projectId, ct);
        if (project is null) return new { error = "not_found", projectId };

        return new
        {
            id = project.Id,
            name = project.Name,
            status = project.Status.ToString(),
            documentCount = project.DocumentCount,
            indexedBytes = project.IndexedBytes,
            lastIndexed = project.LastIndexed,
            commitSha = project.CommitSha,
            lastError = project.LastError
        };
    }

    [McpTool(Name = "reindex_project", IsMutation = true,
        Description = "Trigger a full (re)index of a Librarian project. Returns immediately; indexing runs in the background.")]
    public static async Task<object> ReindexProject(string projectId, IServiceProvider services, CancellationToken ct)
    {
        var project = await Project.Get(projectId, ct);
        if (project is null) return new { started = false, error = "not_found", projectId };

        _ = Task.Run(async () =>
        {
            // The host service provider outlives this call; scope the indexing run independently.
            using var scope = services.CreateScope();
            var index = scope.ServiceProvider.GetRequiredService<IndexProjectAsync>();
            try { await index(projectId, true, CancellationToken.None, null); }
            catch { /* background reindex failures surface via project_status / jobs */ }
        });

        return new { started = true, projectId };
    }

    [McpTool(Name = "get_library_docs",
        Description = "Retrieve grounded, cited code/doc chunks for a project (Context7-compatible semantic search).")]
    public static async Task<object> GetLibraryDocs(
        string libraryId,
        string? topic = null,
        int? tokens = null,
        IServiceProvider? services = null,
        CancellationToken ct = default)
    {
        var project = await Project.Get(libraryId, ct);
        if (project is null) return new { error = "not_found", libraryId };

        using var scope = services!.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<ISearchService>();
        var query = string.IsNullOrWhiteSpace(topic) ? project.Name : topic!;
        var request = SearchRequestContext.Create(
            query, projectIds: new[] { project.Id }, channel: SearchChannel.Mcp, maxTokens: tokens);
        var result = await search.SearchAsync(project.Id, request, ct);

        return new
        {
            libraryId = project.Id,
            query,
            chunks = result.Chunks.Select(c =>
            {
                var file = result.Sources.Files.ElementAtOrDefault(c.Provenance.SourceIndex);
                return new
                {
                    text = c.Text,
                    score = c.Score,
                    source = new
                    {
                        file = file?.FilePath,
                        url = file?.Url,
                        commitSha = file?.CommitSha,
                        startLine = c.Provenance.StartLine,
                        endLine = c.Provenance.EndLine,
                        language = c.Provenance.Language
                    }
                };
            }).ToArray(),
            continuationToken = result.ContinuationToken
        };
    }

    private static object Summarize(Project p) => new
    {
        id = p.Id,
        name = p.Name,
        path = p.RootPath,
        status = p.Status.ToString(),
        documentCount = p.DocumentCount,
        lastIndexed = p.LastIndexed
    };
}
