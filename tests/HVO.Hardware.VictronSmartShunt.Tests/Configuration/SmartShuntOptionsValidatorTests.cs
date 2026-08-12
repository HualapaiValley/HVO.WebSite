using FluentAssertions;
using HVO.Hardware.VictronSmartShunt.Configuration;

namespace HVO.Hardware.VictronSmartShunt.Tests.Configuration;

[TestClass]
public sealed class SmartShuntOptionsValidatorTests
{
    [TestMethod]
    public void Validate_AcceptsProductionDirectCollectorConfiguration()
    {
        var result = new SmartShuntOptionsValidator().Validate(null, new() { Address = "AA:BB:CC:DD:EE:FF", Adapter = "hci0", CentralIngestBaseEndpoint = "https://central.test/" });
        result.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public void Validate_RejectsInvalidMacAdapterAndInsecureCentralEndpoint()
    {
        var result = new SmartShuntOptionsValidator().Validate(null, new() { Address = "bad", Adapter = "bluetooth0", CentralIngestBaseEndpoint = "http://central.test/" });
        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(3);
    }
}
