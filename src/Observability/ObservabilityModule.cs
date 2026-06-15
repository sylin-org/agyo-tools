using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
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
/// <b>What it wires:</b>
/// <list type="bullet">
///   <item><description>An ASP.NET Core health-checks baseline (<see cref="HealthCheckServiceCollectionExtensions.AddHealthChecks(IServiceCollection)"/>).</description></item>
///   <item><description>A resilient named <see cref="System.Net.Http.HttpClient"/> (<c>"agyo-observability"</c>) using the
///     standard .NET resilience handler for outbound probes.</description></item>
///   <item><description>Real OpenTelemetry: a described <c>Resource</c>, tracing
///     (<c>AspNetCore</c> + <c>HttpClient</c> instrumentation) and metrics
///     (<c>AspNetCore</c> + <c>HttpClient</c> + <c>Runtime</c> instrumentation), with the OTLP
///     exporter wired <b>only when an endpoint is configured</b> (see <see cref="OtlpOptions"/>).</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Safe by default:</b> with no <c>Agyo:Observability:Otlp:Endpoint</c> configured the OTLP
/// exporter is not registered, so a consumer with no collector boots cleanly and telemetry is
/// simply collected-but-not-exported. Set the endpoint to start exporting — no other switch.
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

        RegisterOpenTelemetry(services);
    }

    /// <summary>
    /// Wire the real OpenTelemetry SDK: a described resource, tracing + metrics with ASP.NET Core /
    /// HTTP / runtime instrumentation, and an OTLP exporter that is added only when an endpoint is
    /// configured so the host stays bootable without a collector.
    /// </summary>
    private static void RegisterOpenTelemetry(IServiceCollection services)
    {
        // Bind + validate the OTLP options for anyone who wants to resolve them (provenance, tests).
        services
            .AddOptions<OtlpOptions>()
            .BindConfiguration(OtlpOptions.SectionPath)
            .ValidateDataAnnotations();

        // The endpoint decision must be made now (registration time) so we only attach the OTLP
        // exporter when one is configured. Read it from the IConfiguration already present in the
        // collection (the host registers it before module Register() runs). This avoids the OTLP
        // exporter's silent, failing default-export to http://localhost:4317 when no collector exists.
        var otlp = ReadOtlpOptions(services);
        var serviceName = ResolveServiceName(otlp);

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: serviceName,
                serviceVersion: EntryAssemblyVersion(),
                serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (otlp.HasEndpoint)
                {
                    tracing.AddOtlpExporter(exporter => ConfigureOtlpExporter(exporter, otlp));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (otlp.HasEndpoint)
                {
                    metrics.AddOtlpExporter(exporter => ConfigureOtlpExporter(exporter, otlp));
                }
            });
    }

    /// <summary>
    /// Read <see cref="OtlpOptions"/> from the configuration registered in the service collection.
    /// Uses a transient provider built from the registered config sources only — cheap, and the
    /// endpoint must be known at registration time to decide whether to attach the exporter.
    /// </summary>
    private static OtlpOptions ReadOtlpOptions(IServiceCollection services)
    {
        var configuration = FindConfiguration(services);
        if (configuration is null)
        {
            return new OtlpOptions();
        }

        var options = new OtlpOptions();
        configuration.GetSection(OtlpOptions.SectionPath).Bind(options);
        return options;
    }

    /// <summary>
    /// Locate the <see cref="IConfiguration"/> the host already registered (as a singleton instance)
    /// without building the full application service provider.
    /// </summary>
    private static IConfiguration? FindConfiguration(IServiceCollection services)
    {
        // The host registers IConfiguration as a singleton *instance* before module Register() runs,
        // so we can read it straight off the descriptor — no provider build, no side effects.
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(IConfiguration) &&
                descriptor.ImplementationInstance is IConfiguration configuration)
            {
                return configuration;
            }
        }

        return null;
    }

    private static void ConfigureOtlpExporter(OtlpExporterOptions exporter, OtlpOptions otlp)
    {
        if (otlp.HasEndpoint && Uri.TryCreate(otlp.Endpoint, UriKind.Absolute, out var endpoint))
        {
            exporter.Endpoint = endpoint;
        }

        exporter.Protocol = ParseProtocol(otlp.Protocol);
    }

    private static OtlpExportProtocol ParseProtocol(string? protocol)
    {
        if (string.IsNullOrWhiteSpace(protocol))
        {
            return OtlpExportProtocol.Grpc;
        }

        return protocol.Trim().ToLowerInvariant() switch
        {
            "httpprotobuf" or "http/protobuf" or "http" => OtlpExportProtocol.HttpProtobuf,
            _ => OtlpExportProtocol.Grpc,
        };
    }

    private static string ResolveServiceName(OtlpOptions otlp)
        => !string.IsNullOrWhiteSpace(otlp.ServiceName)
            ? otlp.ServiceName!
            : Assembly.GetEntryAssembly()?.GetName().Name ?? "agyo-observability";

    private static string EntryAssemblyVersion()
        => Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";
}
