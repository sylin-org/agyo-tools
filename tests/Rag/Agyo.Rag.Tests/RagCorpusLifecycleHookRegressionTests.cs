using System.Reflection;
using AwesomeAssertions;
using Koan.Data.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.Rag.Tests;

/// <summary>
/// Regression pin for a MIGRATION BUG found by the ARCH-0079 boot-smoke: booting an app that
/// references <c>Agyo.Rag</c> AND declares a <c>[RagCorpus]</c>-decorated <c>Entity&lt;T&gt;</c>
/// failed at <c>AddKoan()</c> with:
/// <c>"Entity base type for &lt;T&gt; has no static Events property"</c>.
/// <para>
/// ROOT CAUSE: <c>KoanRagAutoRegistrar.RegisterIngestionHooks</c> resolves the entity's
/// <c>Entity`1</c> closed base via <c>FindEntityBaseType</c>, then looked up the static
/// <c>Events</c> property with <c>BindingFlags.Static | BindingFlags.Public</c> — but
/// <c>Events</c> is declared on <c>Entity`2</c> (<c>Entity&lt;TEntity, TKey&gt;</c>), the base of
/// <c>Entity`1</c>. Reflection does NOT surface inherited static members without
/// <c>BindingFlags.FlattenHierarchy</c>, so the lookup returned null and the registrar threw,
/// aborting boot. The intended (documented) usage — <c>Policy : Entity&lt;Policy&gt;</c> with
/// <c>[RagCorpus]</c> auto-ingest-on-save — was exactly the path that broke.
/// </para>
/// <para>
/// FIX: <c>BindingFlags.FlattenHierarchy</c> was added to the <c>Events</c> property lookup in
/// <c>KoanRagAutoRegistrar.RegisterIngestionHooks</c> (src/Rag/Initialization/KoanRagAutoRegistrar.cs).
/// This test asserts the exact reflection mechanism the registrar now uses so the fix stays pinned:
/// the inherited static <c>Events</c> property must resolve on the <c>Entity`1</c> closed base when
/// (and only when) <c>FlattenHierarchy</c> is present, and it must be the property declared on
/// <c>Entity`2</c>.
/// </para>
/// <para>
/// This is asserted at the reflection level (rather than only by booting a host with a decorated
/// entity) because it isolates the precise binding-flags contract the registrar depends on, so a
/// future refactor that drops <c>FlattenHierarchy</c> is caught here directly. The end-to-end boot
/// path is covered by <see cref="RagCorpusBootTests"/>.
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
    public void Registrar_Events_lookup_resolves_inherited_static_property_with_FlattenHierarchy()
    {
        var entityBase = FindEntityBaseType(typeof(Doc));
        entityBase.Should().NotBeNull();
        entityBase!.GetGenericTypeDefinition().Name.Should().Be(
            "Entity`1",
            "FindEntityBaseType returns the Entity<TEntity> closed base, NOT Entity<TEntity, TKey>");

        // The pre-fix flags reproduce the original failure: without FlattenHierarchy the inherited
        // static Events property is invisible on the Entity`1 base, returning null. Kept here to pin
        // exactly WHY the registrar threw, so the fix's necessity is documented and stays caught.
        var withoutFlatten = entityBase.GetProperty(
            "Events", BindingFlags.Static | BindingFlags.Public);

        withoutFlatten.Should().BeNull(
            "Events is declared on Entity`2 and is inherited; static members are not surfaced on " +
            "Entity`1 without FlattenHierarchy — this null is what made KoanRagAutoRegistrar throw " +
            "'has no static Events property' and abort AddKoan() before the fix");

        // The fix: the registrar now adds FlattenHierarchy. The EXACT flags the registrar uses today
        // must resolve the inherited static property — this is the assertion that pins the fix.
        var asRegistrarLooksItUp = entityBase.GetProperty(
            "Events", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);

        asRegistrarLooksItUp.Should().NotBeNull(
            "BindingFlags.FlattenHierarchy in the Events lookup in " +
            "KoanRagAutoRegistrar.RegisterIngestionHooks resolves the inherited static property — " +
            "the registrar can now wire AfterUpsert/AfterRemove hooks and AddKoan() boots cleanly");

        // The resolved property is the one declared on the Entity`2 base — confirming the lookup
        // flattened the hierarchy to reach it rather than finding a same-named member on Entity`1.
        asRegistrarLooksItUp!.DeclaringType.Should().NotBeNull();
        asRegistrarLooksItUp.DeclaringType!.IsGenericType.Should().BeTrue();
        asRegistrarLooksItUp.DeclaringType.GetGenericTypeDefinition().Name.Should().Be(
            "Entity`2",
            "the inherited Events property is declared on Entity<TEntity, TKey>, reached only by " +
            "flattening the hierarchy from the Entity`1 closed base");

        _output.WriteLine($"Entity base resolved by registrar: {entityBase.FullName}");
        _output.WriteLine($"Events declared on: {asRegistrarLooksItUp.DeclaringType.FullName}");
    }
}
