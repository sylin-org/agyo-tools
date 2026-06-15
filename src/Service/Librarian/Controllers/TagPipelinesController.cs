using Agyo.Service.Librarian.Filters;
using Agyo.Service.Librarian.Models;
using Agyo.Service.Librarian.Infrastructure;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Agyo.Service.Librarian.Controllers;

/// <summary>
/// Manages tag pipelines that orchestrate rule execution.
/// </summary>
[ApiController]
[Route(Constants.Routes.TagPipelines)]
[ServiceFilter(typeof(PartitionScopeFilter))]
public sealed class TagPipelinesController : EntityController<TagPipeline>
{
    protected override string GetDisplay(TagPipeline entity)
    {
        if (entity is null)
        {
            return "";
        }

        return string.IsNullOrWhiteSpace(entity.Name) ? base.GetDisplay(entity) : entity.Name;
    }
}