using Agyo.Tagging;
using Agyo.Testing.Integration;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.Tagging.Tests;

/// <summary>
/// ARCH-0079 integration suite for the Tagging capability. Every spec boots a real
/// <see cref="AgyoIntegrationHost"/> (a genuine <c>IHost</c> with hosted services started) and calls
/// <c>services.AddKoan()</c> so the migrated entity is wired through Koan's reflective discovery —
/// not hand-registered. Persistence runs through the InMemory data adapter (the capability's
/// declared external infra), which itself is discovered via its own KoanAutoRegistrar.
/// </summary>
public sealed class TaggingIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public TaggingIntegrationTests(ITestOutputHelper output) => _output = output;

    /// <summary>The minimal config that routes the default data source to the in-memory adapter.</summary>
    private static AgyoIntegrationHost.Builder ConfiguredHost() =>
        AgyoIntegrationHost.Configure()
            .WithSetting("Koan:Data:Sources:Default:Adapter", "inmemory")
            .ConfigureServices(services => services.AddKoan());

    /// <summary>
    /// BOOT-SMOKE (mandatory ARCH-0079): the migrated <see cref="Tag"/> entity persists and reloads
    /// through real <c>AddKoan()</c> reflective discovery + the InMemory adapter. If this fails, the
    /// entity did not migrate cleanly into Koan's bootstrap — a genuine migration finding.
    /// </summary>
    [Fact]
    public async Task Tag_entity_round_trips_through_AddKoan_reflective_discovery()
    {
        await using var host = await ConfiguredHost().StartAsync();
        using var _ = AppHost.PushScope(host.Services);

        var tag = new Tag
        {
            Id = "ffxiv",
            DisplayName = "Final Fantasy XIV",
            Description = "Canonical game tag.",
            ParentOf = { "ff14", "final-fantasy-xiv" },
            Parent = "game",
            NoRender = false,
        };

        await tag.Save();

        var loaded = await Tag.Get("ffxiv");

        loaded.Should().NotBeNull("the migrated Tag entity must persist + reload through real AddKoan() discovery");
        loaded!.Id.Should().Be("ffxiv");
        loaded.DisplayName.Should().Be("Final Fantasy XIV");
        loaded.Description.Should().Be("Canonical game tag.");
        loaded.Parent.Should().Be("game");
        loaded.NoRender.Should().BeFalse();
        loaded.ParentOf.Should().BeEquivalentTo("ff14", "final-fantasy-xiv");
    }

    /// <summary>
    /// BEHAVIORAL: a <see cref="TagSet"/> carried on a consuming entity survives a real persist/load
    /// cycle through the InMemory adapter — public and private scopes, multiple open-ended categories,
    /// and the flat <see cref="TagSet.PublicTags"/>/<see cref="TagSet.PrivateTags"/> projections all
    /// reconstitute. This is the value type's serialisation contract proven against real persistence,
    /// not an in-process object equality check.
    /// </summary>
    [Fact]
    public async Task TagSet_round_trips_public_and_private_scopes_through_persistence()
    {
        await using var host = await ConfiguredHost().StartAsync();
        using var _ = AppHost.PushScope(host.Services);

        var thing = new TaggedThing { Id = "package-1" };
        thing.Tags.Public["game"].Set(new[] { "ffxiv", "expedition-33" });
        thing.Tags.Public["technique"].Set("dof").Set("clarity");
        thing.Tags.Private["moderation"].Set("review-pending");

        await thing.Save();

        var loaded = await TaggedThing.Get("package-1");

        loaded.Should().NotBeNull();
        var tags = loaded!.Tags;
        tags.Should().NotBeNull("the TagSet property must round-trip, not deserialise to null");

        // Public scope: both categories and their members survive.
        tags.Has("ffxiv").Should().BeTrue();
        tags.Has("expedition-33").Should().BeTrue();
        tags.Has("dof").Should().BeTrue();
        tags.Has("clarity").Should().BeTrue();
        tags.Find("ffxiv").Should().Be(new TagLocation(TagSet.EScope.Public, "game"));
        tags.Find("dof").Should().Be(new TagLocation(TagSet.EScope.Public, "technique"));

        // Private scope stays private: not visible to a default (Public) query, present under Private.
        tags.Has("review-pending").Should().BeFalse("private tags must not leak into the public scope query");
        tags.Has("review-pending", TagSet.EScope.Private).Should().BeTrue();
        tags.Find("review-pending").Should().Be(new TagLocation(TagSet.EScope.Private, "moderation"));

        // Flat projections — the public surface boundary — reconstitute correctly.
        tags.PublicTags.Should().BeEquivalentTo("ffxiv", "expedition-33", "dof", "clarity");
        tags.PrivateTags.Should().BeEquivalentTo("review-pending");
    }

    /// <summary>
    /// A consuming entity that carries a <see cref="TagSet"/> as a property — exactly the shape the
    /// Tagging capability is designed to be embedded into. Defined in the test assembly so the suite
    /// exercises the real serialisation path for the value type through a discovered entity.
    /// </summary>
    public sealed class TaggedThing : Entity<TaggedThing>
    {
        public TagSet Tags { get; set; } = new();
    }
}
