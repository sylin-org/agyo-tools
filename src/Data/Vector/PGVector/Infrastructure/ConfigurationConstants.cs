namespace Agyo.Data.Vector.PGVector.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the PGVector connector.
/// Eliminates magic "Koan:" string literals across PGVector configuration.
/// </summary>
internal static class ConfigurationConstants
{
    public const string Section = "Agyo:Vector:PGVector";

    /// <summary>
    /// Builds full configuration path: "Agyo:Vector:PGVector:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{Section}:{key}";
}
