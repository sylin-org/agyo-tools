using Agyo.Secrets.Abstractions;
using Agyo.Testing.Integration;
using AwesomeAssertions;
using Koan.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.Secrets.Tests;

/// <summary>
/// Behavioral (no external infra): the always-on resolver chain includes a Configuration provider.
/// Seeding a config value under the <c>Secrets:&lt;scope&gt;:&lt;name&gt;</c> path that the
/// <c>ConfigurationSecretProvider</c> maps from a <c>secret://&lt;scope&gt;/&lt;name&gt;</c> reference
/// must return that value through <see cref="ISecretResolver"/>, while <see cref="SecretValue.ToString"/>
/// stays masked ("***"). Runs entirely in-process via a real AddKoan() host — no container.
/// </summary>
public sealed class ConfigurationSecretResolutionTests
{
    // ConfigurationSecretProvider maps SecretId(scope, name) -> config key "Secrets:<scope>:<name>".
    // SecretId.Parse("secret://config/db-password") yields scope="config", name="db-password".
    private const string SecretReference = "secret://config/db-password";
    private const string ConfigKey = "Secrets:config:db-password";
    private const string SecretText = "p@ssw0rd-s3cr3t";

    private readonly ITestOutputHelper _output;

    public ConfigurationSecretResolutionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Get_ResolvesSeededConfigValue_AndMasksToString()
    {
        BootSmoke.EnsureCapabilityAssemblyLoaded();

        await using var host = await AgyoIntegrationHost.Configure()
            .WithSetting(ConfigKey, SecretText)
            .ConfigureServices(s => s.AddKoan())
            .StartAsync();

        var resolver = host.Services.GetRequiredService<ISecretResolver>();

        var value = await resolver.Get(SecretId.Parse(SecretReference));

        value.AsString().Should().Be(SecretText, "the Configuration provider in the chain resolves the seeded secret");
        value.Meta.Provider.Should().Be("config", "the value must originate from the Configuration provider, not env");

        // Masking contract: ToString() never exposes the material.
        value.ToString().Should().Be("***");
        SecretText.Should().NotBe("***", "guard: the masked form must differ from the real value");

        _output.WriteLine($"Resolved '{SecretReference}' -> AsString length {value.AsString().Length}, ToString '{value}'");
    }

    [Fact]
    public async Task Resolve_ExpandsPlaceholderTemplate_FromConfigChain()
    {
        BootSmoke.EnsureCapabilityAssemblyLoaded();

        await using var host = await AgyoIntegrationHost.Configure()
            .WithSetting(ConfigKey, SecretText)
            .ConfigureServices(s => s.AddKoan())
            .StartAsync();

        var resolver = host.Services.GetRequiredService<ISecretResolver>();

        var expanded = await resolver.Resolve($"connection=${{{SecretReference}}}");

        expanded.Should().Be($"connection={SecretText}",
            "Resolve() must expand ${secret://...} placeholders using the Configuration provider");
    }
}
