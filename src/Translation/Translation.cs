using Koan.Core.Hosting.App;
using Agyo.Translation.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Agyo.Translation;

/// <summary>
/// Static translation API matching Koan's Entity-first patterns.
/// Resolves the in-process <see cref="TranslationService"/> from the ambient host and
/// invokes it directly — no service mesh, no remote routing.
/// </summary>
public static class Translation
{
    private static TranslationService Service
        => AppHost.Current?.GetService<TranslationService>()
           ?? throw new InvalidOperationException(
               "Translation service not registered. Ensure Koan is initialized via services.AddKoan() " +
               "with a reference to Agyo.Translation, and that Koan.AI has a configured chat provider.");

    /// <summary>
    /// Translate text to target language.
    /// </summary>
    /// <param name="text">Text to translate.</param>
    /// <param name="targetLanguage">Target language code (e.g., "es", "fr", "de").</param>
    /// <param name="sourceLanguage">Source language code (optional, "auto" = auto-detect).</param>
    /// <param name="model">AI model to use (optional).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Translation result with detected source language and translated text.</returns>
    public static Task<TranslationResult> Translate(
        string text,
        string targetLanguage,
        string sourceLanguage = "auto",
        string? model = null,
        CancellationToken ct = default)
    {
        var options = new TranslationOptions
        {
            Text = text,
            TargetLanguage = targetLanguage,
            SourceLanguage = sourceLanguage,
            Model = model
        };

        return Service.Translate(options, ct);
    }

    /// <summary>
    /// Translate text using TranslationOptions.
    /// </summary>
    public static Task<TranslationResult> Translate(
        TranslationOptions options,
        CancellationToken ct = default)
        => Service.Translate(options, ct);

    /// <summary>
    /// Detect the language of text.
    /// </summary>
    /// <param name="text">Text to analyze.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Language detection result with ISO 639-1 code.</returns>
    public static Task<LanguageDetectionResult> DetectLanguage(
        string text,
        CancellationToken ct = default)
        => Service.DetectLanguage(text, ct);

    /// <summary>
    /// Get the list of supported languages.
    /// </summary>
    public static Task<SupportedLanguage[]> GetLanguages(CancellationToken ct = default)
        => Service.GetLanguages(ct);
}
