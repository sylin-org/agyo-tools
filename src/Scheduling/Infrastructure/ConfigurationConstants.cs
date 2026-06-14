namespace Agyo.Scheduling.Infrastructure;

internal static class ConfigurationConstants
{
    public const string Section = "Agyo:Scheduling";

    public static class Keys
    {
        public const string Enabled = nameof(Enabled);
        public const string ReadinessGate = nameof(ReadinessGate);
    }

    /// <summary>
    /// Builds full configuration path: "Agyo:Scheduling:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{Section}:{key}";
}
