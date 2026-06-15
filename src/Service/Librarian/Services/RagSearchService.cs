using System.Diagnostics;
using RagApi = Agyo.Rag.Rag;
using Agyo.Rag.Abstractions;
using Agyo.Service.Librarian.Models;
using Koan.Data.Core;
using Microsoft.Extensions.Logging;

namespace Agyo.Service.Librarian.Services;

/// <summary>
/// Re-platformed retrieval (AGYO-0003): a thin persona/token-budget re-ranker over
/// <c>Rag.Corpus&lt;LibraryDoc&gt;().Search</c>. Maps the provenance-bearing <see cref="RagChunk"/>s back into
/// the Librarian's cited <see cref="SearchResult"/> shape (file/line/url/commit). Because Rag writes
/// vectors inline at ingest, the chunks are immediately searchable — this is what closes the bespoke
/// async-outbox search gap.
/// </summary>
public sealed class RagSearchService : ISearchService
{
    private const int MaxChunks = 20;
    private readonly ILogger<RagSearchService> _logger;

    public RagSearchService(ILogger<RagSearchService> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<SearchResult> SearchAsync(string projectId, SearchRequestContext request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        IReadOnlyList<RagChunk> ragChunks;
        using (EntityContext.Partition(projectId))
        {
            ragChunks = await RagApi.Corpus<LibraryDoc>().Search(request.Query, MaxChunks, cancellationToken);
        }

        var fileIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sourceFiles = new List<SourceFile>();
        var chunks = new List<SearchResultChunk>();

        var tokenBudget = Math.Clamp(request.MaxTokens, 1000, 20000);
        var tokensUsed = 0;

        foreach (var rc in ragChunks)
        {
            var provenance = rc.Provenance;
            var filePath = provenance?.FilePath ?? rc.DocumentId;

            if (!fileIndex.TryGetValue(filePath, out var sourceIndex))
            {
                sourceIndex = sourceFiles.Count;
                fileIndex[filePath] = sourceIndex;
                sourceFiles.Add(new SourceFile(
                    FilePath: filePath,
                    Title: provenance?.FilePath is null ? null : Path.GetFileName(filePath),
                    Url: provenance?.SourceUrl,
                    CommitSha: provenance?.CommitSha ?? ""));
            }

            // Token-budget truncation (persona MaxTokens), highest-scored first.
            var estimatedTokens = (rc.Text.Length / 4) + 1;
            if (chunks.Count > 0 && tokensUsed + estimatedTokens > tokenBudget) break;
            tokensUsed += estimatedTokens;

            chunks.Add(new SearchResultChunk(
                Id: rc.ChunkId,
                Text: rc.Text,
                Score: (float)rc.Score,
                Provenance: new ChunkProvenance(
                    SourceIndex: sourceIndex,
                    StartByteOffset: 0,
                    EndByteOffset: 0,
                    StartLine: provenance?.StartLine ?? 0,
                    EndLine: provenance?.EndLine ?? 0,
                    Language: provenance?.Language),
                Reasoning: null));
        }

        sw.Stop();
        return new SearchResult(
            Chunks: chunks,
            Metadata: new SearchMetadata(
                TokensRequested: tokenBudget,
                TokensReturned: tokensUsed,
                Page: 1,
                Model: "all-minilm",
                VectorProvider: "weaviate",
                Timestamp: DateTime.UtcNow,
                Duration: sw.Elapsed),
            Sources: new SearchSources(sourceFiles.Count, sourceFiles),
            Insights: null,
            ContinuationToken: null,
            Warnings: Array.Empty<string>());
    }
}
