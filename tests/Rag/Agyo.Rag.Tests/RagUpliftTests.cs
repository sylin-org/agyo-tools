using Agyo.Rag;
using Agyo.Rag.Abstractions;
using AwesomeAssertions;
using Xunit;

namespace Agyo.Rag.Tests;

/// <summary>
/// Unit specs for the AGYO-0003 code-intelligence uplifts into Agyo.Rag: chunk provenance
/// (round-trip parsing + the additive RagChunk field), the per-query retrieval knobs, and
/// gitignore-aware repo discovery. (The provenance vector round-trip + knob effects are covered
/// behaviorally by the infra-gated ingest/search specs.)
/// </summary>
public sealed class RagUpliftTests
{
    [Fact]
    public void Provenance_FromMetadata_tolerates_json_round_trip_boxing()
    {
        var meta = new Dictionary<string, object>
        {
            [RagChunkProvenance.Keys.FilePath] = "src/Foo.cs",
            [RagChunkProvenance.Keys.StartLine] = 10L,   // JSON round-trip boxes ints as long
            [RagChunkProvenance.Keys.EndLine] = "25",    // ...or as string
            [RagChunkProvenance.Keys.Language] = "csharp"
        };

        var prov = RagChunkProvenance.FromMetadata(meta);

        prov.Should().NotBeNull();
        prov!.FilePath.Should().Be("src/Foo.cs");
        prov.StartLine.Should().Be(10);
        prov.EndLine.Should().Be(25);
        prov.Language.Should().Be("csharp");
        prov.HasValue.Should().BeTrue();
    }

    [Fact]
    public void Provenance_FromMetadata_is_null_when_absent()
    {
        RagChunkProvenance.FromMetadata(null).Should().BeNull();
        RagChunkProvenance.FromMetadata(new Dictionary<string, object> { ["unrelated"] = "x" }).Should().BeNull();
    }

    [Fact]
    public void RagChunk_provenance_is_additive_and_optional()
    {
        var cited = new RagChunk("c1", "d1", "text", 0.9, Provenance: new RagChunkProvenance(FilePath: "a.cs"));
        cited.Provenance!.FilePath.Should().Be("a.cs");

        // The existing positional constructor still compiles unchanged (backward-compatible).
        new RagChunk("c2", "d2", "t", 0.5).Provenance.Should().BeNull();
    }

    [Fact]
    public void Query_knobs_default_to_null_so_global_config_wins()
    {
        var defaults = new RagQueryOptions();
        defaults.HybridAlpha.Should().BeNull();
        defaults.RerankTopN.Should().BeNull();
        defaults.MaxContextTokens.Should().BeNull();

        var tuned = new RagQueryOptions { HybridAlpha = 0.2, RerankTopN = 5, MaxContextTokens = 4000 };
        tuned.HybridAlpha.Should().Be(0.2);
        tuned.RerankTopN.Should().Be(5);
        tuned.MaxContextTokens.Should().Be(4000);
    }

    [Fact]
    public void Discover_finds_code_and_docs_excluding_build_output_and_gitignored()
    {
        var root = Path.Combine(Path.GetTempPath(), "rag-discover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "node_modules"));
        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "# readme");
            File.WriteAllText(Path.Combine(root, "src", "Foo.cs"), "class Foo {}");
            File.WriteAllText(Path.Combine(root, "bin", "Foo.dll"), "binary");
            File.WriteAllText(Path.Combine(root, "node_modules", "pkg.js"), "x");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "ignored.txt\n");
            File.WriteAllText(Path.Combine(root, "ignored.txt"), "secret");

            var found = Rag.Discover(root).Select(Path.GetFileName).ToList();

            found.Should().Contain("README.md");
            found.Should().Contain("Foo.cs");
            found.Should().NotContain("Foo.dll", "bin/ is excluded");
            found.Should().NotContain("pkg.js", "node_modules/ is excluded");
            found.Should().NotContain("ignored.txt", ".gitignore is honored");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
