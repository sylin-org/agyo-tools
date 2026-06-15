using Agyo.Service.Librarian.Filters;
using Agyo.Service.Librarian.Models;
using Agyo.Service.Librarian.Infrastructure;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Agyo.Service.Librarian.Controllers;

/// <summary>
/// Manages declarative tag rules used by tag pipelines to infer metadata.
/// </summary>
[ApiController]
[Route(Constants.Routes.TagRules)]
[ServiceFilter(typeof(PartitionScopeFilter))]
public sealed class TagRulesController : EntityController<TagRule>
{
    protected override string GetDisplay(TagRule entity)
    {
        if (entity is null)
        {
            return "";
        }

        return string.IsNullOrWhiteSpace(entity.Name) ? base.GetDisplay(entity) : entity.Name;
    }
}