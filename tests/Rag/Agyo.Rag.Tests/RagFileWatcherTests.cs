using Agyo.Rag;
using AwesomeAssertions;
using Xunit;

namespace Agyo.Rag.Tests;

/// <summary>
/// Unit specs for the AGYO-0003 uplift-4 file-watch incremental ingester. Drives the debounce/dedup/filter
/// core directly via the public change seams (no real FileSystemWatcher events — those are OS-flaky), so the
/// behavior is deterministic: only ingestable files reach the ingest callback, duplicates coalesce, and the
/// batch is emitted once. Included files are created on disk because discovery filtering (correctly) only
/// matches files that exist — exactly the state a Created/Changed event reports.
/// </summary>
public sealed class RagFileWatcherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rag-watch-" + Guid.NewGuid().ToString("N"));

    private string Touch(string relative)
    {
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "// content");
        return full;
    }

    private RagFileWatcher Build(List<IReadOnlyList<string>> batches, RagFileWatcherOptions? opts = null)
        => new(
            _root,
            opts ?? new RagFileWatcherOptions(),
            (paths, _) => { batches.Add(paths); return Task.CompletedTask; });

    [Fact]
    public async Task Flush_ingests_only_discovery_matched_files()
    {
        Directory.CreateDirectory(_root);
        var foo = Touch(Path.Combine("src", "Foo.cs"));   // included (.cs)
        var readme = Touch("README.md");                   // included (README*)
        var dll = Touch(Path.Combine("bin", "Foo.dll"));   // excluded (bin/ + .dll)
        var js = Touch(Path.Combine("node_modules", "p.js")); // excluded (node_modules/)
        var png = Touch("image.png");                      // not an included type

        var batches = new List<IReadOnlyList<string>>();
        using var watcher = Build(batches);

        watcher.NotifyChanged(foo);
        watcher.NotifyChanged(readme);
        watcher.NotifyChanged(dll);
        watcher.NotifyChanged(js);
        watcher.NotifyChanged(png);

        await watcher.FlushAsync();

        batches.Should().HaveCount(1);
        batches[0].Select(Path.GetFileName).Should().BeEquivalentTo(new[] { "Foo.cs", "README.md" });
    }

    [Fact]
    public async Task Repeated_changes_to_same_file_coalesce_into_one_entry()
    {
        var file = Touch(Path.Combine("src", "Service.cs"));
        var batches = new List<IReadOnlyList<string>>();
        using var watcher = Build(batches);

        watcher.NotifyChanged(file);
        watcher.NotifyChanged(file);
        watcher.NotifyChanged(file);

        await watcher.FlushAsync();

        batches.Should().HaveCount(1);
        batches[0].Should().HaveCount(1, "the same file changed three times must ingest once");
    }

    [Fact]
    public async Task Flush_is_a_noop_when_nothing_pending()
    {
        Directory.CreateDirectory(_root);
        var batches = new List<IReadOnlyList<string>>();
        using var watcher = Build(batches);

        await watcher.FlushAsync();

        batches.Should().BeEmpty();
    }

    [Fact]
    public async Task Ingest_failures_are_routed_to_the_error_callback_not_thrown()
    {
        var file = Touch("a.cs");
        Exception? captured = null;
        using var watcher = new RagFileWatcher(
            _root,
            new RagFileWatcherOptions(),
            (_, _) => throw new InvalidOperationException("ingest boom"),
            ex => captured = ex);

        watcher.NotifyChanged(file);
        var act = async () => await watcher.FlushAsync();

        await act.Should().NotThrowAsync();
        captured.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("ingest boom");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }
}
