using System.ComponentModel.DataAnnotations;

namespace Agyo.Observability;

/// <summary>
/// Bound options for the OpenTelemetry OTLP (OpenTelemetry Protocol) exporter, read from the
/// configuration section <see cref="SectionPath"/> (<c>Agyo:Observability:Otlp</c>).
/// </summary>
/// <remarks>
/// <para>
/// The exporter is <b>opt-in by endpoint</b>: when <see cref="Endpoint"/> is left unset
/// (the default), the OTLP exporter is NOT registered at all, so the module boots cleanly with
/// no collector present and never attempts a (failing) export to the default
/// <c>http://localhost:4317</c>. Configure an <see cref="Endpoint"/> to start exporting.
/// </para>
/// <para>
/// This makes the safe default "no collector required to boot" while keeping a single switch
/// (the endpoint) to turn real export on.
/// </para>
/// </remarks>
public sealed class OtlpOptions
{
    /// <summary>The configuration section these options bind from: <c>Agyo:Observability:Otlp</c>.</summary>
    public const string SectionPath = "Agyo:Observability:Otlp";

    /// <summary>
    /// OTLP collector endpoint (e.g. <c>http://localhost:4318</c> for HTTP/protobuf or
    /// <c>http://localhost:4317</c> for gRPC). When null/empty, the OTLP exporter is not wired and
    /// telemetry is collected but not exported — no collector is required for the host to boot.
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// OTLP transport protocol. <c>"grpc"</c> (default) maps to
    /// <see cref="OpenTelemetry.Exporter.OtlpExportProtocol.Grpc"/>; <c>"httpprotobuf"</c> (or
    /// <c>"http/protobuf"</c>) maps to <see cref="OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf"/>.
    /// </summary>
    public string? Protocol { get; init; }

    /// <summary>
    /// Logical service name reported on the OpenTelemetry <c>Resource</c>. When null/empty, the
    /// entry-assembly name is used as a sensible default.
    /// </summary>
    public string? ServiceName { get; init; }

    /// <summary>True when an <see cref="Endpoint"/> is configured and the exporter should be wired.</summary>
    public bool HasEndpoint => !string.IsNullOrWhiteSpace(Endpoint);
}
