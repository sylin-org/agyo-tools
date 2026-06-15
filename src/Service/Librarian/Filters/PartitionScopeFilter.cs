using System.Threading.Tasks;
using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Agyo.Service.Librarian.Filters;

/// <summary>
/// Ensures EntityContext operations run against the global partition for Agyo.Service.Librarian endpoints.
/// </summary>
public sealed class PartitionScopeFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        using var scope = EntityContext.With(partition: null);
        await next();
    }
}
