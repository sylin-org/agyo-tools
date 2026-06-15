namespace Agyo.Rag.Content.Discovery;

/// <summary>
/// Gitignore-aware repository file enumeration. Produces absolute paths — exactly the shape
/// <c>IRagCorpus.Ingest(IEnumerable&lt;string&gt;)</c> consumes. Dependency-free (no FileSystemGlobbing)
/// for predictable behavior across host frameworks; include/exclude globs are interpreted as the
/// conventional <c>**/*.ext</c> / <c>name*</c> / <c>**/dir/**</c> shapes.
/// </summary>
internal static class RepoDiscovery
{
    public static IEnumerable<string> Enumerate(string repoRoot, RepoDiscoveryOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(options);

        if (!Path.IsPathFullyQualified(repoRoot))
            throw new ArgumentException("Repo root must be an absolute path.", nameof(repoRoot));
        if (repoRoot.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("UNC paths are not supported.", nameof(repoRoot));
        if (!Directory.Exists(repoRoot))
            return [];

        var includeExts = ExtensionsFrom(options.IncludeGlobs);
        var includeNamePrefixes = NamePrefixesFrom(options.IncludeGlobs);
        var excludeExts = ExtensionsFrom(options.ExcludeGlobs);
        var excludeDirs = DirectoriesFrom(options.ExcludeGlobs);
        var gitignoreSubstrings = options.RespectGitignore ? ReadGitignore(repoRoot) : [];

        return EnumerateCore(repoRoot, options, includeExts, includeNamePrefixes, excludeExts, excludeDirs, gitignoreSubstrings);
    }

    /// <summary>
    /// True when a single (absolute) <paramref name="path"/> under <paramref name="repoRoot"/> would be
    /// included by discovery. Used by the file-watch incremental ingester so a change event is filtered by
    /// exactly the same include/exclude/gitignore rules as a full <see cref="Enumerate"/> sweep.
    /// </summary>
    public static bool IsMatch(string repoRoot, string path, RepoDiscoveryOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);

        return Matches(
            repoRoot, path, options,
            ExtensionsFrom(options.IncludeGlobs), NamePrefixesFrom(options.IncludeGlobs),
            ExtensionsFrom(options.ExcludeGlobs), DirectoriesFrom(options.ExcludeGlobs),
            options.RespectGitignore ? ReadGitignore(repoRoot) : []);
    }

    private static IEnumerable<string> EnumerateCore(
        string repoRoot, RepoDiscoveryOptions options,
        HashSet<string> includeExts, List<string> includeNamePrefixes,
        HashSet<string> excludeExts, HashSet<string> excludeDirs, IReadOnlyList<string> gitignore)
    {
        foreach (var path in SafeEnumerate(repoRoot))
        {
            if (Matches(repoRoot, path, options, includeExts, includeNamePrefixes, excludeExts, excludeDirs, gitignore))
                yield return path;
        }
    }

    // The single-path include decision shared by Enumerate (full sweep) and IsMatch (one change event).
    private static bool Matches(
        string repoRoot, string path, RepoDiscoveryOptions options,
        HashSet<string> includeExts, List<string> includeNamePrefixes,
        HashSet<string> excludeExts, HashSet<string> excludeDirs, IReadOnlyList<string> gitignore)
    {
        var relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Directory excludes (any path segment except the file name).
        if (segments.Length > 1 && segments[..^1].Any(seg => excludeDirs.Contains(seg))) return false;

        var ext = Path.GetExtension(path);
        if (ext.Length > 0 && excludeExts.Contains(ext)) return false;

        if (gitignore.Count > 0 && gitignore.Any(g => relative.Contains(g, StringComparison.OrdinalIgnoreCase))) return false;

        var name = Path.GetFileName(path);
        var included =
            (ext.Length > 0 && includeExts.Contains(ext)) ||
            includeNamePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (!included) return false;

        return PassesFileFilters(path, options);
    }

    private static IEnumerable<string> SafeEnumerate(string root)
    {
        try { return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories); }
        catch { return []; }
    }

    private static HashSet<string> ExtensionsFrom(IEnumerable<string> globs)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var glob in globs)
        {
            var idx = glob.LastIndexOf("*.", StringComparison.Ordinal);
            if (idx >= 0 && !glob.EndsWith("/**", StringComparison.Ordinal))
                result.Add(glob[(idx + 1)..]); // "**/*.cs" -> ".cs"
        }
        return result;
    }

    private static List<string> NamePrefixesFrom(IEnumerable<string> globs)
    {
        var result = new List<string>();
        foreach (var glob in globs)
        {
            // Bare name prefix like "README*" / "CHANGELOG*" (no path separator, no leading "*.").
            if (!glob.Contains('/') && glob.EndsWith('*') && !glob.StartsWith("*.", StringComparison.Ordinal))
                result.Add(glob.TrimEnd('*'));
        }
        return result;
    }

    private static HashSet<string> DirectoriesFrom(IEnumerable<string> globs)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var glob in globs)
        {
            // "**/bin/**" -> "bin"
            if (glob.StartsWith("**/", StringComparison.Ordinal) && glob.EndsWith("/**", StringComparison.Ordinal))
            {
                var inner = glob[3..^3];
                if (inner.Length > 0 && !inner.Contains('/')) result.Add(inner);
            }
        }
        return result;
    }

    private static List<string> ReadGitignore(string repoRoot)
    {
        var gitignore = Path.Combine(repoRoot, ".gitignore");
        var result = new List<string>();
        if (!File.Exists(gitignore)) return result;

        foreach (var raw in File.ReadLines(gitignore))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('!')) continue;
            result.Add(line.Trim('/'));
        }
        return result;
    }

    private static bool PassesFileFilters(string path, RepoDiscoveryOptions options)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return false;
            if (options.MaxFileSizeBytes > 0 && info.Length > options.MaxFileSizeBytes) return false;

            var attributes = info.Attributes;
            if (options.SkipHidden && attributes.HasFlag(FileAttributes.Hidden)) return false;
            if (options.SkipSymlinks && attributes.HasFlag(FileAttributes.ReparsePoint)) return false;

            return true;
        }
        catch
        {
            return false;
        }
    }
}
