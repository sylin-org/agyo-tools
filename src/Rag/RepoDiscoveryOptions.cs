namespace Agyo.Rag;

/// <summary>
/// Knobs for <see cref="Rag.Discover(string)"/> — gitignore-aware repository discovery that produces
/// the file-path list for <c>Rag.Corpus&lt;T&gt;().Ingest(...)</c>. Zero-config defaults cover common
/// code + docs file types and exclude build/vendor output.
/// </summary>
/// <remarks>
/// Discovery is gitignore-<em>aware</em>, not fully gitignore-compliant: simple exclude lines are
/// honored; negation (<c>!pattern</c>) and anchored rules are not. This matches the proven behavior of
/// the bespoke discovery it was harvested from.
/// </remarks>
public sealed record RepoDiscoveryOptions
{
    /// <summary>Globs (relative to the repo root) to include. Default: common code + docs types.</summary>
    public IReadOnlyList<string> IncludeGlobs { get; init; } = DefaultIncludes;

    /// <summary>Globs to exclude. Default: build/vendor/VCS output.</summary>
    public IReadOnlyList<string> ExcludeGlobs { get; init; } = DefaultExcludes;

    /// <summary>Read <c>&lt;root&gt;/.gitignore</c> and exclude its (non-negated) entries. Default: true.</summary>
    public bool RespectGitignore { get; init; } = true;

    /// <summary>Skip files with the Hidden attribute. Default: true.</summary>
    public bool SkipHidden { get; init; } = true;

    /// <summary>Skip reparse points / symlinks. Default: true.</summary>
    public bool SkipSymlinks { get; init; } = true;

    /// <summary>Skip files larger than this (bytes). 0 = no cap. Default: 0.</summary>
    public long MaxFileSizeBytes { get; init; }

    // NOTE: declared BEFORE Default so the static-init order populates them first (Default = new()
    // reads these as the IncludeGlobs/ExcludeGlobs defaults).
    public static readonly IReadOnlyList<string> DefaultIncludes = new[]
    {
        "**/*.cs", "**/*.ts", "**/*.tsx", "**/*.js", "**/*.jsx", "**/*.mjs", "**/*.py", "**/*.go",
        "**/*.rs", "**/*.java", "**/*.kt", "**/*.rb", "**/*.php", "**/*.c", "**/*.h", "**/*.cpp",
        "**/*.hpp", "**/*.cc", "**/*.swift", "**/*.sql", "**/*.sh", "**/*.md", "**/*.markdown",
        "**/*.rst", "**/*.txt", "README*", "CHANGELOG*", "**/*.yml", "**/*.yaml", "**/*.json", "**/*.toml"
    };

    public static readonly IReadOnlyList<string> DefaultExcludes = new[]
    {
        "**/bin/**", "**/obj/**", "**/node_modules/**", "**/.git/**", "**/dist/**", "**/build/**",
        "**/target/**", "**/__pycache__/**", "**/.pytest_cache/**", "**/.vs/**", "**/.vscode/**",
        "**/.next/**", "**/.nuxt/**", "**/packages/**", "**/vendor/**", "**/coverage/**", "**/.koan/**",
        "**/*.dll", "**/*.exe", "**/*.pdb", "**/*.min.js", "**/*.lock"
    };

    /// <summary>The zero-config defaults.</summary>
    public static RepoDiscoveryOptions Default { get; } = new();
}
