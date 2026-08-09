using FluentAssertions;
using HVO.Hardware.Eg4.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HVO.Hardware.Eg4.Tests.Configuration;

[TestClass]
public sealed class Eg4OptionsValidatorTests
{
    [TestMethod]
    public void Validate_MultipleSameTypeAndSharedPortWithDistinctUnits_IsValid()
    {
        var options = new Eg4Options { Devices = [Device("a", 1), Device("b", 2)] };

        new Eg4OptionsValidator(Environment("Production")).Validate(null, options).Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public void Validate_ReorderAddAndDisable_DoNotChangeConfiguredIdentity()
    {
        var first = Device("east", 1);
        var second = Device("west", 2);
        var reordered = new[] { Device("new", 3), second, first };
        second.Enabled = false;

        reordered.Single(device => device.Alias == "east").SourceId.Should().Be("eg4-east");
        reordered.Single(device => device.Alias == "east").DeviceId.Should().Be("inverter-east");
        reordered.Single(device => device.Alias == "west").SourceId.Should().Be("eg4-west");
    }

    [TestMethod]
    public void Validate_DuplicateIdentityAndEndpoint_IsInvalidEvenWhenDisabled()
    {
        var duplicate = Device("dup", 1);
        duplicate.SourceId = "EG4-EAST";
        duplicate.Enabled = false;
        var options = new Eg4Options { Devices = [Device("east", 1), duplicate] };

        var result = new Eg4OptionsValidator(Environment("Production")).Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(message => message.Contains("SourceId", StringComparison.Ordinal));
        result.Failures.Should().Contain(message => message.Contains("configured more than once", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validate_RejectsUnsupportedTypeUnstablePortUnitAndPollInterval()
    {
        var device = Device("bad", 0);
        device.Type = (Eg4DeviceType)999;
        device.Port = "/dev/ttyUSB0";
        device.PollIntervalSeconds = 5;

        var result = new Eg4OptionsValidator(Environment("Production"))
            .Validate(null, new Eg4Options { Devices = [device] });

        result.Failures.Should().HaveCount(4);
    }

    [TestMethod]
    public void Validate_SimulationOnlyAllowsTestingOrDevelopment()
    {
        var options = new Eg4Options { SimulationEnabled = true };

        new Eg4OptionsValidator(Environment("Testing")).Validate(null, options).Succeeded.Should().BeTrue();
        new Eg4OptionsValidator(Environment("Development")).Validate(null, options).Succeeded.Should().BeTrue();
        new Eg4OptionsValidator(Environment("Production")).Validate(null, options).Failed.Should().BeTrue();
        new Eg4OptionsValidator(Environment("Staging")).Validate(null, options).Failed.Should().BeTrue();
    }

    [TestMethod]
    public void Validate_NullDeviceAndPortReturnFailures()
    {
        var noPort = Device("no-port", 1);
        noPort.Port = null!;
        var result = new Eg4OptionsValidator(Environment("Production"))
            .Validate(null, new Eg4Options { Devices = [null!, noPort] });

        result.Failures.Should().Contain(message => message.Contains("null entries", StringComparison.Ordinal));
        result.Failures.Should().Contain(message => message.Contains("stable /dev/serial/by-id", StringComparison.Ordinal));
    }

    private static Eg4DeviceOptions Device(string alias, byte unitId) => new()
    {
        Type = Eg4DeviceType.Inverter6500Ex,
        SourceId = $"eg4-{alias}",
        DeviceId = $"inverter-{alias}",
        Alias = alias,
        Port = "/dev/serial/by-id/usb-eg4-bus",
        UnitId = unitId,
    };

    private static TestEnvironment Environment(string name) => new() { EnvironmentName = name };

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "HVO.Hardware.Eg4.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
