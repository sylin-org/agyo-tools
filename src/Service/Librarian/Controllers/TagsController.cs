using Agyo.Service.Librarian.Filters;
using Agyo.Service.Librarian.Models;
using Agyo.Service.Librarian.Infrastructure;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Agyo.Service.Librarian.Controllers;

/// <summary>
/// Manages tag vocabulary entries that drive rule resolution and search experiences.
/// </summary>
[ApiController]
[Route(Constants.Routes.Tags)]
[ServiceFilter(typeof(PartitionScopeFilter))]
public sealed class TagsController : EntityController<TagVocabularyEntry>
{
	protected override string GetDisplay(TagVocabularyEntry entity)
	{
		if (entity is null)
		{
			return "";
		}

		return string.IsNullOrWhiteSpace(entity.Tag) ? base.GetDisplay(entity) : entity.Tag;
	}
}
