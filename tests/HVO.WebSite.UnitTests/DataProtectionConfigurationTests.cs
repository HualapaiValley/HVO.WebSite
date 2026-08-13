using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class DataProtectionConfigurationTests
{
    [TestMethod]
    public void ConfigureDataProtection_AllowsFrameworkDefaultsWhenNoRepositoryIsConfigured()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        var act = () => HVO.WebSite.v9.Program.ConfigureDataProtection(services, configuration);

        act.Should().NotThrow();
    }

    [TestMethod]
    public void ConfigureDataProtection_AllowsProtectedFileSystemRepository()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            ("DataProtection:KeysDirectory", "/tmp/hvo-data-protection-tests"),
            ("DataProtection:KeyIdentifier", "https://example.vault.azure.net/keys/data-protection"));

        var act = () => HVO.WebSite.v9.Program.ConfigureDataProtection(services, configuration);

        act.Should().NotThrow();
    }

    [TestMethod]
    public void ConfigureDataProtection_RejectsPersistedKeysWithoutEncryptor()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            ("DataProtection:KeysDirectory", "/tmp/hvo-data-protection-tests"));

        var act = () => HVO.WebSite.v9.Program.ConfigureDataProtection(services, configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*KeyIdentifier*encryption at rest*");
    }

    [TestMethod]
    public void ConfigureDataProtection_RejectsEncryptorWithoutRepository()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            ("DataProtection:KeyIdentifier", "https://example.vault.azure.net/keys/data-protection"));

        var act = () => HVO.WebSite.v9.Program.ConfigureDataProtection(services, configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*KeyIdentifier requires KeysDirectory or BlobUri*");
    }

    [TestMethod]
    public void ConfigureDataProtection_RejectsMultipleRepositories()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            ("DataProtection:KeysDirectory", "/tmp/hvo-data-protection-tests"),
            ("DataProtection:BlobUri", "https://example.blob.core.windows.net/data-protection/keys.xml"),
            ("DataProtection:KeyIdentifier", "https://example.vault.azure.net/keys/data-protection"));

        var act = () => HVO.WebSite.v9.Program.ConfigureDataProtection(services, configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*only one Data Protection key repository*");
    }

    private static IConfiguration CreateConfiguration(params (string Key, string Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(value => value.Key, value => (string?)value.Value))
            .Build();
    }
}
