using Microsoft.Extensions.Configuration;

namespace HVO.WebSite.v9.Configuration;

internal static class HostConfigurationPrecedence
{
    public const string DatabaseConnectionName = "HualapaiValleyObservatory";

    public sealed record DataProtectionSettings(
        string? ApplicationName,
        string? KeysDirectory,
        string? BlobUri,
        string? KeyIdentifier);

    public static void Restore(ConfigurationManager configuration, string? hostDatabaseConnection)
    {
        // Never allow the legacy Key Vault value to become a database fallback.
        // The normal database registration will fail startup when this is blank.
        configuration[$"ConnectionStrings:{DatabaseConnectionName}"] = hostDatabaseConnection ?? string.Empty;
    }

    public static DataProtectionSettings CaptureDataProtection(IConfiguration configuration)
    {
        return new DataProtectionSettings(
            configuration["DataProtection:ApplicationName"],
            configuration["DataProtection:KeysDirectory"],
            configuration["DataProtection:BlobUri"],
            configuration["DataProtection:KeyIdentifier"]);
    }

    public static void RestoreDataProtection(
        ConfigurationManager configuration,
        DataProtectionSettings hostSettings)
    {
        if (string.IsNullOrWhiteSpace(hostSettings.KeysDirectory))
        {
            return;
        }

        configuration["DataProtection:ApplicationName"] = hostSettings.ApplicationName;
        configuration["DataProtection:KeysDirectory"] = hostSettings.KeysDirectory;
        configuration["DataProtection:BlobUri"] = hostSettings.BlobUri;
        configuration["DataProtection:KeyIdentifier"] = hostSettings.KeyIdentifier;
    }
}
