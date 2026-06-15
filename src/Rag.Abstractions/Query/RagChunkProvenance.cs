using System.Globalization;

namespace Agyo.Rag.Abstractions;

/// <summary>
/// Agnostic source provenance for a retrieved chunk — where in the corpus the text came from.
/// Populated for file-ingested corpora (the file path is known at ingest) and surfaced on
/// <see cref="RagChunk.Provenance"/>. All fields are nullable: provenance is best-effort and an
/// entity-ingested chunk may have none.
/// </summary>
public sealed record RagChunkProvenance(
    string? FilePath = null,
    int? StartLine = null,
    int? EndLine = null,
    string? Language = null,
    string? CommitSha = null,
    string? SourceUrl = null)
{
    /// <summary>True when this provenance carries any source information.</summary>
    public bool HasValue =>
        FilePath is not null || StartLine is not null || EndLine is not null ||
        Language is not null || CommitSha is not null || SourceUrl is not null;

    /// <summary>Well-known metadata keys mirrored into the chunk's open <see cref="RagChunk.Metadata"/> bag.</summary>
    public static class Keys
    {
        public const string FilePath = "source_path";
        public const string StartLine = "line_start";
        public const string EndLine = "line_end";
        public const string Language = "language";
        public const string CommitSha = "commit_sha";
        public const string SourceUrl = "source_url";
    }

    /// <summary>
    /// Build provenance from a round-tripped vector metadata bag. Tolerant of the value boxing the
    /// JSON round-trip produces (string/long/int/double/JToken), so a naive cast never throws.
    /// </summary>
    public static RagChunkProvenance? FromMetadata(IReadOnlyDictionary<string, object>? metadata)
    {
        if (metadata is null || metadata.Count == 0) return null;

        var provenance = new RagChunkProvenance(
            FilePath: AsString(metadata, Keys.FilePath),
            StartLine: AsInt(metadata, Keys.StartLine),
            EndLine: AsInt(metadata, Keys.EndLine),
            Language: AsString(metadata, Keys.Language),
            CommitSha: AsString(metadata, Keys.CommitSha),
            SourceUrl: AsString(metadata, Keys.SourceUrl));

        return provenance.HasValue ? provenance : null;
    }

    private static string? AsString(IReadOnlyDictionary<string, object> m, string key)
    {
        if (!m.TryGetValue(key, out var value) || value is null) return null;
        var s = value as string ?? value.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static int? AsInt(IReadOnlyDictionary<string, object> m, string key)
    {
        if (!m.TryGetValue(key, out var value) || value is null) return null;
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            _ => int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : null
        };
    }
}
