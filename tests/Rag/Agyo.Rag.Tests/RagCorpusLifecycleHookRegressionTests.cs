using System.Reflection;
using AwesomeAssertions;
using Koan.Data.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.Rag.Tests;

/// <summary>
/// Regression pin for a MIGRATION BUG found by the ARCH-0079 boot-smoke: booting an app that
/// references <c>Agyo.Rag</c> AND declares a <c>[RagCorpus]</c>-decorated <c>Entity&lt;T&gt;</c>
/// fails at <c>AddKoan()</c> with:
/// <c>"Entity base type for &lt;T&gt; has no static Events property"</c>.
/// <para>
/// ROOT CAUSE: <c>KoanRagAutoRegistrar.RegisterIngestionHooks</c> resolves the entity's
/// <c>Entity`1</c> closed base via <c>FindEntityBaseType</c>, then looks up the static
/// <c>Events</c> property with <c>BindingFlags.Static | BindingFlags.Public</c> — but
/// <c>Events</c> is declared on <c>Entity`2</c> (<c>Entity&lt;TEntity, TKey&gt;</c>), the base of
/// <c>Entity`1</c>. Reflection does NOT surface inherited static members without
/// <c>BindingFlags.FlattenHierarchy</c>, so the lookup returns null and the registrar throws,
/// aborting boot. The intended (documented) usage — <c>Policy : Entity&lt;Policy&gt;</c> with
/// <c>[RagCorpus]</c> auto-ingest-on-save — is exactly the path that breaks.
/// </para>
/// <para>
/// FIX: add <c>BindingFlags.FlattenHierarchy</c> to the <c>Events</c> property lookup in
/// <c>KoanRagAutoRegistrar.RegisterIngestionHooks</c> (src/Rag/Initialization/KoanRagAutoRegistrar.cs).
/// This test asserts the exact reflection mechanism so it stays caught after the fix:
/// once flattened, the lookup must succeed.
/// </para>
/// <para>
/// This is asserted at the reflection level rather than by booting a host with a decorated entity,
/// because the registrar's <c>DiscoverRagCorpusTypes</c> scans the whole assembly — a single
/// <c>[RagCorpus]</c> type here would poison every other <c>AddKoan()</c> boot in this assembly.
/// </para>
/// </summary>
public sealed class RagCorpusLifecycleHookRegressionTests
{
    private readonly ITestOutputHelper _output;

    public RagCorpusLifecycleHookRegressionTests(ITestOutputHelper output) => _output = output;

    // Stand-in for a user's [RagCorpus] entity. Same shape the registrar reflects over.
    private sealed class Doc : Entity<Doc>
    {
        public string Body { get; set; } = string.Empty;
    }

    // Mirror of KoanRagAutoRegistrar.FindEntityBaseType (returns the Entity`1 closed base).
    private static Type? FindEntityBaseType(Type type)
    {
        var current = type;
        while (current is not null)
        {
            if (current.IsGenericType)
            {
                var n = current.GetGenericTypeDefinition().Name;
                if (n is "Entity`1" or "Entity`2") return current;
            }
            current = current.BaseType;
        }
        return null;
    }

    [Fact]
    public void Registrar_Events_lookup_misses_inherited_static_property_without_FlattenHierarchy()
    {
        var entityBase = FindEntityBaseType(typeof(Doc));
        entityBase.Should().NotBeNull();
        entityBase!.GetGenericTypeDefinition().Name.Should().Be(
            "Entity`1",
            "FindEntityBaseType returns the Entity<TEntity> closed base, NOT Entity<TEntity, TKey>");

        // The registrar's EXACT current flags — reproduces the boot failure (returns null → throw).
        var asRegistrarLooksItUp = entityBase.GetProperty(
            "Events", BindingFlags.Static | BindingFlags.Public);

        asRegistrarLooksItUp.Should().BeNull(
            "Events is declared on Entity`2 and is inherited; static members are not surfaced on " +
            "Entity`1 without FlattenHierarchy — this null is what makes KoanRagAutoRegistrar throw " +
            "'has no static Events property' and abort AddKoan()");

        // The fix: add FlattenHierarchy. Then the property resolves and hook wiring can proceed.
        var withFix = entityBase.GetProperty(
            "Events", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);

        withFix.Should().NotBeNull(
            "adding BindingFlags.FlattenHierarchy to the Events lookup in " +
            "KoanRagAutoRegistrar.RegisterIngestionHooks resolves the inherited static property");

        _output.WriteLine($"Entity base resolved by registrar: {entityBase.FullName}");
        _output.WriteLine($"Events declared on: {withFix!.DeclaringType?.FullName}");
    }
}
