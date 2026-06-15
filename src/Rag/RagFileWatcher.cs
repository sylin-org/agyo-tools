using Agyo.Rag.Content.Discovery;

namespace Agyo.Rag;

/// <summary>
/// Knobs for <see cref="RagFileWatcher"/> / <see cref="Rag.Watch{TEntity}"/>.
/// </summary>
public sealed record RagFileWatcherOptions
{
    /// <summary>The discovery rules used to decide which changed files are ingestable. Default: the same
    /// zero-config code+docs set as <see cref="Rag.Discover(string)"/>.</summary>
    public RepoDiscoveryOptions Discovery { get; init; } = RepoDiscoveryOptions.Default;

    /// <summary>Coalesce a burst of change events into one ingest after this quiet window. Default: 500ms.</summary>
    public TimeSpan DebounceWindow { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Watch nested directories. Default: true.</summary>
    public bool IncludeSubdirectories { get; init; } = true;
}

/// <summary>
/// Opt-in incremental ingest (AGYO-0003 uplift 4): watch a repository root and re-ingest changed files into a
/// corpus so a long-running consumer keeps its index fresh without a bespoke monitor. Change events are
/// filtered by the SAME discovery rules as a full sweep (<see cref="RepoDiscovery.IsMatch"/>), de-duplicated,
/// and debounced into a single ingest call.
/// <para>
/// Obtain via <c>Rag.Watch&lt;Doc&gt;(repoRoot)</c>; dispose to stop. Created/Changed/Renamed files are
/// re-ingested (re-ingesting a path re-indexes it). Deletes are not handled — the corpus has no remove-by-path
/// (entity removal only); a full <c>Rebuild()</c> reconciles deletions.
/// </para>
/// </summary>
public sealed class RagFileWatcher : IDisposable
{
    private readonly string _root;
    private readonly RagFileWatcherOptions _options;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task> _ingest;
    private readonly Action<Exception>? _onError;

    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _fsw;
    private Timer? _debounce;
    private bool _disposed;

    public RagFileWatcher(
        string root,
        RagFileWatcherOptions options,
        Func<IReadOnlyList<string>, CancellationToken, Task> ingest,
        Action<Exception>? onError = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ingest = ingest ?? throw new ArgumentNullException(nameof(ingest));
        _onError = onError;
    }

    /// <summary>Begin watching. Idempotent.</summary>
    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RagFileWatcher));
        if (_fsw is not null || !Directory.Exists(_root)) return;

        _fsw = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = _options.IncludeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _fsw.Created += OnFsEvent;
        _fsw.Changed += OnFsEvent;
        _fsw.Renamed += OnFsRenamed;
        _fsw.Error += (_, e) => _onError?.Invoke(e.GetException());
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => NotifyChanged(e.FullPath);
    private void OnFsRenamed(object sender, RenamedEventArgs e) => NotifyChanged(e.FullPath);

    /// <summary>
    /// Record an ingestable change (also the FileSystemWatcher callback). Filters by the same discovery rules
    /// as a full sweep, de-duplicates, and schedules a debounced flush. Public so a consumer can drive the
    /// watcher from its own change source (e.g. a git hook) without the built-in FileSystemWatcher.
    /// </summary>
    public void NotifyChanged(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return;
        if (Directory.Exists(fullPath)) return; // directory event, not a file
        if (!RepoDiscovery.IsMatch(_root, fullPath, _options.Discovery)) return;

        lock (_gate)
        {
            if (_disposed) return;
            _pending.Add(Path.GetFullPath(fullPath));
            _debounce?.Dispose();
            _debounce = new Timer(_ => _ = FlushAsync(), null, _options.DebounceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Drain the pending change set and ingest it as one batch now (the debounce timer also calls
    /// this). No-op when nothing is pending; ingest failures are routed to the error callback.</summary>
    public async Task FlushAsync()
    {
        string[] batch;
        lock (_gate)
        {
            if (_pending.Count == 0) return;
            batch = new string[_pending.Count];
            _pending.CopyTo(batch);
            _pending.Clear();
        }

        try
        {
            await _ingest(batch, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _debounce?.Dispose();
            _debounce = null;
        }

        if (_fsw is not null)
        {
            _fsw.EnableRaisingEvents = false;
            _fsw.Created -= OnFsEvent;
            _fsw.Changed -= OnFsEvent;
            _fsw.Renamed -= OnFsRenamed;
            _fsw.Dispose();
            _fsw = null;
        }
    }
}
