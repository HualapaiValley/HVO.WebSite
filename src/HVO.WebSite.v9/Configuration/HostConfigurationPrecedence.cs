using Microsoft.Extensions.Configuration;

namespace HVO.WebSite.v9.Configuration;

internal static class HostConfigurationPrecedence
{
    public const string DatabaseConnectionName = "HualapaiValleyObservatory";

    public static void Restore(ConfigurationManager configuration, string? hostDatabaseConnection)
    {
        // Never allow the legacy Key Vault value to become a database fallback.
        // The normal database registration will fail startup when this is blank.
        configuration[$"ConnectionStrings:{DatabaseConnectionName}"] = hostDatabaseConnection ?? string.Empty;
    }
}
