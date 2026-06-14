namespace Agyo.Secrets.Connector.Vault.Internal;

internal static class VaultConstants
{
    public const string ConfigPath = "Agyo:Secrets:Vault";
    public const string HttpClientName = "Agyo.Secrets.Connector.Vault";

    public static class Keys
    {
        public const string Address = ConfigPath + ":Address";
        public const string DisableAutoDetection = ConfigPath + ":DisableAutoDetection";
    }
}
