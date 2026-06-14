using System.Linq.Expressions;
using Koan.Data.Abstractions.Filtering;

namespace Agyo.Web.GraphQl.Filtering;

/// <summary>
/// Composes the user-supplied JSON filter with hook-contributed LINQ predicates (WEB-0068) into a
/// single normalized <see cref="Filter"/> using AND semantics. Hook predicates are lowered to the
/// unified AST via <see cref="LinqFilterCompiler"/> so everything converges on one representation
/// before reaching the orchestrator — no parameter-rebinding gymnastics, no Expression/AST split.
/// </summary>
/// <remarks>
/// Ported into Agyo.Web.GraphQl from the (package-internal) Koan.Web.Filtering.QueryFilterComposer
/// so this opt-in package depends only on Koan's PUBLIC surface (Koan.Data.Abstractions.Filtering:
/// <see cref="Filter"/>, <see cref="AllOf"/>, <see cref="LinqFilterCompiler"/>). Keep in sync with the
/// Koan.Web original if its semantics change.
/// </remarks>
internal static class QueryFilterComposer
{
    public static Filter? AndAll<TEntity>(
        Filter? userFilter,
        IReadOnlyList<LambdaExpression> hookPredicates)
    {
        var all = new List<Filter>();
        if (userFilter is not null) all.Add(userFilter);
        if (hookPredicates is { Count: > 0 })
            foreach (var p in hookPredicates)
            {
                var typed = p as Expression<Func<TEntity, bool>>
                    ?? throw new InvalidOperationException(
                        $"QueryOptions.Predicates entry was {p?.GetType().FullName ?? "null"}, expected " +
                        $"Expression<Func<{typeof(TEntity).FullName}, bool>>. Use QueryOptions.AddPredicate<TEntity>(...).");
                all.Add(LinqFilterCompiler.Compile(typed));
            }

        return all.Count switch
        {
            0 => null,
            1 => all[0],
            _ => new AllOf(all)
        };
    }
}
