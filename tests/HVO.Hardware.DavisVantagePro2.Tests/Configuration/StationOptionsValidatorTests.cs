using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting;
using HVO.Hardware.DavisVantagePro2.Configuration;

namespace HVO.Hardware.DavisVantagePro2.Tests.Configuration;

[TestClass]
public sealed class StationOptionsValidatorTests
{
    [TestMethod]
    public void Validate_AcceptsCompleteMountedConfiguration()
    {
        var result = new StationOptionsValidator(Identity("station-1", "hvo")).Validate(null, Valid());
        result.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public void Validate_RejectsMissingSiteAndSourceMismatch()
    {
        var result = new StationOptionsValidator(Identity("other", null)).Validate(null, Valid());
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(message => message.Contains("StationId", StringComparison.Ordinal));
        result.Failures.Should().Contain(message => message.Contains("SiteId", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validate_RejectsMissingLegacyMigrationOffset()
    {
        var options = Valid();
        options.LegacyArchiveConsoleUtcOffsetHours = null;

        var result = new StationOptionsValidator(Identity("station-1", "hvo")).Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(message => message.Contains("LegacyArchiveConsoleUtcOffsetHours", StringComparison.Ordinal));
    }

    private static StationOptions Valid() => new()
    {
        Host = "davis-console.local",
        StationId = "station-1",
        CentralIngestBaseEndpoint = "https://example.test/",
        LegacyArchiveConsoleUtcOffsetHours = -7,
        LocalDatabasePath = "/app/data/davis-local.db",
    };

    private static EdgeRuntimeIdentity Identity(string sourceId, string? siteId) => new(
        "hvo-davis", "1", "instance", "davis", "davis-vantage-pro2", GatewayDomain.Weather,
        sourceId, siteId, "station-1", "Testing", "host", "Davis");
}
