using Microsoft.Extensions.DependencyInjection;
using Koan.Core;

namespace Agyo.Observability;

/// <summary>
/// Opt-in observability baseline for Koan-built applications, re-expressed as a
/// <see cref="KoanModule"/> (ARCH-0086) — referencing the package wires it (Reference = Intent).
/// </summary>
/// <remarks>
/// This is the migrated successor to the old <c>ObservabilityRecipe</c> (an <c>IKoanRecipe</c>),
/// re-expressed as a module so it rides the framework's source-generated discovery + ordering
/// directly, with no recipe-discovery (<c>AppDomain.GetAssemblies()</c> scan) machinery.
/// <para>
/// <b>What it wires today:</b>
/// <list type="bullet">
///   <item><description>An ASP.NET Core health-checks baseline (<see cref="HealthCheckServiceCollectionExtensions.AddHealthChecks(IServiceCollection)"/>).</description></item>
///   <item><description>A resilient named <see cref="System.Net.Http.HttpClient"/> (<c>"agyo-observability"</c>) using the
///     standard .NET resilience handler for outbound probes.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>TODO (OTel):</b> the original recipe advertised OpenTelemetry but never implemented it.
/// OTel wiring is intentionally NOT faked here. When an honest implementation lands (or a
/// dedicated <c>Sylin.Koan.Observability</c> package is extracted), wire it explicitly in
/// <see cref="Register"/>. Until then this module ships as health-checks + resilient-HttpClient only.
/// </para>
/// </remarks>
public sealed class ObservabilityModule : KoanModule
{
    /// <summary>The named <see cref="System.Net.Http.HttpClient"/> registered for outbound observability probes.</summary>
    public const string HttpClientName = "agyo-observability";

    /// <inheritdoc />
    public override string Id => "observability";

    /// <inheritdoc />
    public override void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Health-checks baseline. ASP.NET Core's AddHealthChecks is idempotent and safe to call
        // even when no app pipeline maps the endpoint — it simply registers the service.
        services.AddHealthChecks();

        // Resilient named HttpClient for outbound probes. AddStandardResilienceHandler applies the
        // built-in resilience pipeline (retry, circuit breaker, timeout) introduced in .NET 8/9.
        services
            .AddHttpClient(HttpClientName)
            .AddStandardResilienceHandler();

        // TODO(OTel): the legacy recipe advertised OpenTelemetry but never implemented it.
        // Do NOT fake it — wire real OTel (or consume an extracted Sylin.Koan.Observability) here
        // when an honest implementation exists.
    }
}
