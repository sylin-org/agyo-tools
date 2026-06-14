using System;

namespace Agyo.Testing.Infrastructure;

/// <summary>
/// Minimal skip-clean gate for integration specs that need external infrastructure (a pgvector
/// Postgres, an Ollama endpoint, a vector DB). The operator supplies a connection string / endpoint
/// via an environment variable; when absent the spec skips cleanly rather than failing.
/// </summary>
/// <remarks>
/// This is the first rung of Koan's container probe ladder (explicit env-var connection string).
/// Full Testcontainers auto-provisioning can be layered on later; until then a behavioral spec runs
/// only when its infra env-var is set, and is reported skipped otherwise (pair with
/// <c>Xunit.SkippableFact</c>: <c>Skip.IfNot(InfraProbe.Available("AGYO_..."))</c>).
/// </remarks>
public static class InfraProbe
{
    /// <summary>Return the first non-empty value among the named environment variables, or null.</summary>
    public static string? ConnectionString(params string[] envVars)
    {
        foreach (var name in envVars)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    /// <summary>True when at least one of the named environment variables is set to a non-empty value.</summary>
    public static bool Available(params string[] envVars) => ConnectionString(envVars) is not null;

    /// <summary>A human-readable reason for a skip, naming the env vars the operator can set.</summary>
    public static string Unavailable(params string[] envVars)
        => $"infrastructure not provided — set one of: {string.Join(", ", envVars)}";
}
