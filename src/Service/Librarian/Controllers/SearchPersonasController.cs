using Agyo.Service.Librarian.Filters;
using Agyo.Service.Librarian.Models;
using Agyo.Service.Librarian.Infrastructure;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Agyo.Service.Librarian.Controllers;

/// <summary>
/// Manages search personas that define retrieval defaults and boosts.
/// </summary>
[ApiController]
[Route(Constants.Routes.SearchPersonas)]
[ServiceFilter(typeof(PartitionScopeFilter))]
public sealed class SearchPersonasController : EntityController<SearchPersona>
{
    protected override string GetDisplay(SearchPersona entity)
    {
        if (entity is null)
        {
            return "";
        }

        return string.IsNullOrWhiteSpace(entity.DisplayName) ? entity.Name : entity.DisplayName;
    }
}