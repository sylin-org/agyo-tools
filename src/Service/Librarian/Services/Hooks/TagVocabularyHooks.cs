using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Agyo.Service.Librarian.Models;
using Agyo.Service.Librarian.Infrastructure;
using Koan.Web.Hooks;
using Microsoft.Extensions.Caching.Memory;
using AgyoTag = Agyo.Tagging.Tag;

namespace Agyo.Service.Librarian.Services.Hooks;

/// <summary>
/// Normalizes the canonical tag vocabulary (Sylin.Agyo.Tagging.Tag) on write and keeps the
/// TagResolver's vocabulary cache coherent. <c>Tag.Id</c> is the canonical form; <c>Tag.ParentOf</c>
/// is its synonym list (AGYO-0002 converge).
/// </summary>
public sealed class TagVocabularyHooks : IModelHook<AgyoTag>
{
    private readonly IMemoryCache _cache;

    public TagVocabularyHooks(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public int Order => 0;

    public Task OnBeforeFetch(HookContext<AgyoTag> ctx, string id)
        => Task.CompletedTask;

    public Task OnAfterFetch(HookContext<AgyoTag> ctx, AgyoTag? model)
        => Task.CompletedTask;

    public Task OnBeforeSave(HookContext<AgyoTag> ctx, AgyoTag model)
    {
        Normalize(model);
        return Task.CompletedTask;
    }

    public Task OnAfterSave(HookContext<AgyoTag> ctx, AgyoTag model)
    {
        Invalidate();
        return Task.CompletedTask;
    }

    public Task OnBeforeDelete(HookContext<AgyoTag> ctx, AgyoTag model)
        => Task.CompletedTask;

    public Task OnAfterDelete(HookContext<AgyoTag> ctx, AgyoTag model)
    {
        Invalidate();
        return Task.CompletedTask;
    }

    public Task OnBeforePatch(HookContext<AgyoTag> ctx, string id, object patch)
        => Task.CompletedTask;

    public Task OnAfterPatch(HookContext<AgyoTag> ctx, AgyoTag model)
    {
        Normalize(model);
        Invalidate();
        return Task.CompletedTask;
    }

    private static void Normalize(AgyoTag tag)
    {
        if (string.IsNullOrWhiteSpace(tag.Id))
        {
            throw new ValidationException("Tag id (the canonical form) cannot be empty.");
        }

        tag.Id = tag.Id.Trim().ToLowerInvariant();
        tag.DisplayName = string.IsNullOrWhiteSpace(tag.DisplayName) ? null : tag.DisplayName.Trim();
        tag.ParentOf = TagEnvelope.NormalizeTags(tag.ParentOf).ToList();
    }

    private void Invalidate() => _cache.Remove(Constants.CacheKeys.TagVocabulary);
}
