using Agyo.Service.Librarian.Filters;
using Agyo.Service.Librarian.Infrastructure;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Agyo.Service.Librarian.Controllers;

/// <summary>
/// Manages the canonical tag vocabulary + synonym registry (Sylin.Agyo.Tagging.Tag) that drives rule
/// resolution and search experiences. <c>Tag.Id</c> is the canonical form; <c>Tag.ParentOf</c> its synonyms.
/// </summary>
[ApiController]
[Route(Constants.Routes.Tags)]
[ServiceFilter(typeof(PartitionScopeFilter))]
public sealed class TagsController : EntityController<Agyo.Tagging.Tag>
{
	protected override string GetDisplay(Agyo.Tagging.Tag entity)
	{
		if (entity is null)
		{
			return "";
		}

		return string.IsNullOrWhiteSpace(entity.DisplayName) ? base.GetDisplay(entity) : entity.DisplayName!;
	}
}
