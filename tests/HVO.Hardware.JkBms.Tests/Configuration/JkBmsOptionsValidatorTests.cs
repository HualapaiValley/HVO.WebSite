using FluentAssertions;
using HVO.Hardware.JkBms.Configuration;

namespace HVO.Hardware.JkBms.Tests.Configuration;

[TestClass]
public sealed class JkBmsOptionsValidatorTests
{
    [TestMethod]
    public void Validate_StableUniqueFleet_IsValid()
    {
        var result = new JkBmsOptionsValidator().Validate(null, ValidOptions(
            Device("01", "bank-1a"),
            Device("02", "bank-1b")));

        result.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public void Validate_DuplicateIdentityAndAddress_IsInvalidEvenWhenDisabled()
    {
        var duplicate = Device("01", "BANK-1A");
        duplicate.Enabled = false;
        var result = new JkBmsOptionsValidator().Validate(null, ValidOptions(Device("01", "bank-1a"), duplicate));

        result.Failures.Should().Contain(message => message.Contains("Address", StringComparison.Ordinal));
        result.Failures.Should().Contain(message => message.Contains("DeviceId", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validate_RejectsMalformedMacAdapterIdentityAndInsecureIngest()
    {
        var device = Device("01", "bank-1a");
        device.Address = "not-a-mac";
        device.HciAdapter = "bluetooth0";
        device.Alias = " bank ";
        device.PollIntervalSeconds = 5;
        var options = ValidOptions(device);
        options.HciAdapter = "hci";
        options.CentralIngestEndpoint = "http://central.test/api/v1/bms/readings";

        var result = new JkBmsOptionsValidator().Validate(null, options);

        result.Failures.Should().HaveCount(6);
    }

    private static JkBmsOptions ValidOptions(params BmsDeviceConfig[] devices) => new()
    {
        CentralIngestEndpoint = "https://central.test/api/v1/bms/readings",
        Devices = [.. devices],
    };

    private static BmsDeviceConfig Device(string suffix, string id) => new()
    {
        Address = $"AA:BB:CC:DD:EE:{suffix}",
        DeviceId = id,
        Alias = id,
    };
}
