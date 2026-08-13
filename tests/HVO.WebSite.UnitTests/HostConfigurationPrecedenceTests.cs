using FluentAssertions;
using HVO.WebSite.v9.Configuration;
using Microsoft.Extensions.Configuration;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class HostConfigurationPrecedenceTests
{
    [TestMethod]
    public void Restore_KeepsHostDatabaseConnectionAboveLaterCloudProvider()
    {
        const string localConnection = "Server=mssql;Database=HualapaiValleyObservatory;";
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{HostConfigurationPrecedence.DatabaseConnectionName}"] = "Server=cloud.database.windows.net;",
        });

        HostConfigurationPrecedence.Restore(configuration, localConnection);

        configuration.GetConnectionString(HostConfigurationPrecedence.DatabaseConnectionName).Should().Be(localConnection);
    }

    [TestMethod]
    public void Restore_BlankHostConnectionDisablesCloudDatabaseFallback()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{HostConfigurationPrecedence.DatabaseConnectionName}"] = "Server=cloud.database.windows.net;",
        });

        HostConfigurationPrecedence.Restore(configuration, null);

        configuration.GetConnectionString(HostConfigurationPrecedence.DatabaseConnectionName).Should().BeEmpty();
    }

    [TestMethod]
    public void RestoreDataProtection_KeepsMountedRepositoryAboveLaterCloudProvider()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:ApplicationName"] = "HVO.WebSite.v9",
            ["DataProtection:KeysDirectory"] = "/mounted/keys",
            ["DataProtection:BlobUri"] = string.Empty,
            ["DataProtection:KeyIdentifier"] = "https://current.vault.azure.net/keys/data-protection",
        });
        var hostSettings = HostConfigurationPrecedence.CaptureDataProtection(configuration);
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:KeysDirectory"] = string.Empty,
            ["DataProtection:BlobUri"] = "https://legacy.blob.core.windows.net/keys.xml",
            ["DataProtection:KeyIdentifier"] = "https://legacy.vault.azure.net/keys/data-protection",
        });

        HostConfigurationPrecedence.RestoreDataProtection(configuration, hostSettings);

        configuration["DataProtection:ApplicationName"].Should().Be("HVO.WebSite.v9");
        configuration["DataProtection:KeysDirectory"].Should().Be("/mounted/keys");
        configuration["DataProtection:BlobUri"].Should().BeEmpty();
        configuration["DataProtection:KeyIdentifier"].Should().Be("https://current.vault.azure.net/keys/data-protection");
    }

    [TestMethod]
    public void RestoreDataProtection_LeavesCloudRepositoryWhenNoHostMountIsConfigured()
    {
        var configuration = new ConfigurationManager();
        var hostSettings = HostConfigurationPrecedence.CaptureDataProtection(configuration);
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:BlobUri"] = "https://cloud.blob.core.windows.net/keys.xml",
        });

        HostConfigurationPrecedence.RestoreDataProtection(configuration, hostSettings);

        configuration["DataProtection:BlobUri"].Should().Be("https://cloud.blob.core.windows.net/keys.xml");
    }
}
