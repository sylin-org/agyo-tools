namespace Agyo.Rag.Abstractions;

/// <summary>
/// Full options for a RAG query. Tier 8 escape hatch — most developers
/// use <c>Ask(query)</c> or <c>Ask(query, focus)</c> instead.
/// </summary>
public sealed record RagQueryOptions
{
    /// <summary>
    /// Natural-language focus string that shapes retrieval and generation.
    /// Influences what the agent searches for, how results are reranked,
    /// and how the answer is framed.
    /// </summary>
    public string? Focus { get; init; }

    /// <summary>
    /// Structured tactical hints for the retrieval agent.
    /// </summary>
    public RetrievalHint? Hint { get; init; }

    /// <summary>
    /// Minimum confidence threshold for the answer. If the agent cannot
    /// meet this threshold, the result status is <see cref="RagQueryStatus.LowConfidence"/>.
    /// Default: 0.5.
    /// </summary>
    public double MinConfidence { get; init; } = 0.5;

    /// <summary>
    /// Pin a specific retrieval strategy. Use <see cref="RetrievalStrategy.Auto"/>
    /// for production; pin a specific strategy for deterministic evaluation.
    /// </summary>
    public RetrievalStrategy Strategy { get; init; } = RetrievalStrategy.Auto;

    /// <summary>
    /// Whether to include source citations in the result.
    /// Default: true.
    /// </summary>
    public bool IncludeCitations { get; init; } = true;

    /// <summary>
    /// Optional metadata filter scoping retrieval (AI-0036 P1-AI). The unified
    /// <see cref="Koan.Data.Abstractions.Filtering.Filter"/> AST — build it with
    /// <c>Filter.All(Filter.Eq("tenant", t), Filter.In("tag", tags))</c>. <c>null</c> = match-all
    /// (today's behaviour). An unsupported operator/field fails loud at retrieval, never silently.
    /// </summary>
    public Koan.Data.Abstractions.Filtering.Filter? Filter { get; init; }

    /// <summary>
    /// Per-query hybrid search alpha override (0 = pure keyword/BM25 … 1 = pure semantic/vector).
    /// <c>null</c> = use the global <c>RagOptions.HybridAlpha</c> default. Lets a caller tune the
    /// keyword/semantic blend per query without changing global config.
    /// </summary>
    public double? HybridAlpha { get; init; }

    /// <summary>
    /// Per-query rerank top-N override (how many chunks feed generation / are returned).
    /// <c>null</c> = use the global <c>RagOptions.RerankTopN</c> default.
    /// </summary>
    public int? RerankTopN { get; init; }

    /// <summary>
    /// Optional token budget for the assembled context window: when set, the retrieved context is
    /// trimmed (highest-scored chunks first) to roughly this many tokens. <c>null</c> = no budget.
    /// </summary>
    public int? MaxContextTokens { get; init; }
}
