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
}
