using System;
using System.Threading.Tasks;
using Agyo.Testing.Infrastructure;
using Agyo.Testing.Integration;
using Agyo.Translation;
using Agyo.Translation.Models;
using AwesomeAssertions;
using Koan.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.Translation.Tests;

/// <summary>
/// ARCH-0079 integration specs for the Translation capability.
/// </summary>
/// <remarks>
/// Boots a real <see cref="Microsoft.Extensions.Hosting.IHost"/> through
/// <see cref="AgyoIntegrationHost"/> with <c>services.AddKoan()</c>, so the
/// <c>Agyo.Translation.Initialization.KoanAutoRegistrar</c> is found by genuine reflective
/// discovery (not hand-registration). The boot-smoke proves <see cref="TranslationService"/>
/// resolves; the behavioral spec is gated on a real Ollama endpoint.
/// </remarks>
public sealed class TranslationIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public TranslationIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// BOOT-SMOKE (ARCH-0079): AddKoan() reflective discovery runs the Translation
    /// KoanAutoRegistrar, which registers the in-process <see cref="TranslationService"/> singleton.
    /// No external infrastructure required.
    /// </summary>
    [Fact]
    public async Task AddKoan_Discovers_And_Registers_TranslationService()
    {
        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var service = host.Services.GetService<TranslationService>();

        service.Should().NotBeNull(
            "the Agyo.Translation KoanAutoRegistrar registers TranslationService as a singleton " +
            "and AddKoan() must discover it via reflective bootstrap (ARCH-0079)");
    }

    /// <summary>
    /// BOOT-SMOKE: the registered <see cref="TranslationService"/> exposes its in-process,
    /// AI-free surface (GetLanguages) without any chat provider configured — proving the service
    /// is genuinely live, not just a type registration.
    /// </summary>
    [Fact]
    public async Task TranslationService_GetLanguages_Returns_Catalog()
    {
        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var service = host.Services.GetRequiredService<TranslationService>();

        var languages = await service.GetLanguages();

        languages.Should().NotBeNullOrEmpty();
        languages.Should().Contain(l => l.Code == "es",
            "Spanish is part of the static supported-language catalog");
    }

    /// <summary>
    /// GUARD (no AI required): the translate path must fail fast on caller errors — null, empty,
    /// or whitespace <see cref="TranslationOptions.Text"/> — by throwing <see cref="ArgumentException"/>
    /// BEFORE any chat provider is touched. Runs unconditionally (plain [Fact]); needs no Ollama
    /// because the guard validates input at method entry, ahead of every AI call.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task Translate_With_Null_Or_Blank_Text_Throws_ArgumentException(string? text)
    {
        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var service = host.Services.GetRequiredService<TranslationService>();

        var options = new TranslationOptions
        {
            Text = text!,
            TargetLanguage = "es",
            SourceLanguage = "en"
        };

        // ArgumentNullException derives from ArgumentException, so this also covers the
        // null-options contract; the blank-text cases throw ArgumentException directly.
        await FluentActions
            .Awaiting(() => service.Translate(options))
            .Should().ThrowAsync<ArgumentException>(
                "null/empty/whitespace Text is a caller error that must fail fast before any AI call");
    }

    /// <summary>
    /// GUARD (no AI required): a null <see cref="TranslationOptions"/> argument must throw
    /// <see cref="ArgumentNullException"/> at method entry, before any chat provider is touched.
    /// </summary>
    [Fact]
    public async Task Translate_With_Null_Options_Throws_ArgumentNullException()
    {
        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var service = host.Services.GetRequiredService<TranslationService>();

        await FluentActions
            .Awaiting(() => service.Translate(null!))
            .Should().ThrowAsync<ArgumentNullException>(
                "a null options argument is a caller error that must fail fast before any AI call");
    }

    /// <summary>
    /// BEHAVIORAL: configure the Koan AI Ollama endpoint from AGYO_OLLAMA_ENDPOINT, then call the
    /// static <see cref="Translation.Translate(string, string, string, string?, System.Threading.CancellationToken)"/>
    /// facade (which resolves the live TranslationService from AppHost.Current) and assert a
    /// non-empty translation. Skips cleanly when no Ollama endpoint is provided.
    /// </summary>
    [SkippableFact]
    public async Task Translate_Short_String_Returns_NonEmpty_Output()
    {
        Skip.IfNot(
            InfraProbe.Available("AGYO_OLLAMA_ENDPOINT"),
            InfraProbe.Unavailable("AGYO_OLLAMA_ENDPOINT"));

        var endpoint = InfraProbe.ConnectionString("AGYO_OLLAMA_ENDPOINT")!;
        // Optional model override; falls back to the connector default (llama3.2) when unset.
        var model = InfraProbe.ConnectionString("AGYO_OLLAMA_MODEL");

        _output.WriteLine($"Using Ollama endpoint: {endpoint}");

        var builder = AgyoIntegrationHost.Configure()
            // Provide the endpoint via both the explicit connection string and the URL list so the
            // contributor wires the adapter regardless of which discovery branch it takes.
            .WithSetting("Koan:Ai:Ollama:ConnectionString", endpoint)
            .WithSetting("Koan:Ai:Ollama:Urls:0", endpoint)
            .WithSetting("Koan:Ai:AutoDiscoveryEnabled", "true")
            .WithSetting("Koan:Ai:AllowDiscoveryInNonDev", "true");

        if (!string.IsNullOrWhiteSpace(model))
        {
            builder = builder.WithSetting("Koan:Ai:Ollama:DefaultModel", model);
        }

        await using var host = await builder
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        // Sanity: the service is live in this host before exercising the AI round-trip.
        host.Services.GetService<TranslationService>().Should().NotBeNull();

        // Use the static facade exactly as a consumer would — it resolves from AppHost.Current.
        TranslationResult result = await Translation.Translate(
            text: "Hello, world",
            targetLanguage: "es",
            sourceLanguage: "en",
            model: model);

        result.Should().NotBeNull();
        result.TranslatedText.Should().NotBeNullOrWhiteSpace(
            "a configured Ollama endpoint should return a non-empty translation");
        result.TargetLanguage.Should().Be("es");

        _output.WriteLine($"Translated -> {result.TranslatedText}");
    }
}
