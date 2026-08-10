using FluentAssertions;
using HVO.Hardware.Eg4.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HVO.Hardware.Eg4.Tests.Configuration;

[TestClass]
public sealed class Eg4OptionsValidatorTests
{
    [TestMethod]
    public void Validate_MultipleControllersOnStablePortsAtUnitOne_IsValid()
    {
        var options = new Eg4Options
        {
            Devices =
            [
                Device("a", 1, Eg4DeviceType.ChargeControllerMppt10048Hv, "/dev/serial/by-id/usb-eg4-a"),
                Device("b", 1, Eg4DeviceType.ChargeControllerMppt10048Hv, "/dev/serial/by-id/usb-eg4-b"),
            ],
        };

        new Eg4OptionsValidator(Environment("Production")).Validate(null, options).Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public void Validate_ReorderAddAndDisable_DoNotChangeConfiguredIdentity()
    {
        var first = Device("east", 0, port: "/dev/hvo/eg4-east");
        var second = Device("west", 0, port: "/dev/hvo/eg4-west");
        var reordered = new[] { Device("new", 0, port: "/dev/hvo/eg4-new"), second, first };
        second.Enabled = false;

        reordered.Single(device => device.Alias == "east").SourceId.Should().Be("eg4-east");
        reordered.Single(device => device.Alias == "east").DeviceId.Should().Be("inverter-east");
        reordered.Single(device => device.Alias == "west").SourceId.Should().Be("eg4-west");
    }

    [TestMethod]
    public void Validate_DuplicateIdentityAndEndpoint_IsInvalidEvenWhenDisabled()
    {
        var duplicate = Device("dup", 0, port: "/dev/hvo/eg4-east");
        duplicate.SourceId = "EG4-EAST";
        duplicate.Enabled = false;
        var options = new Eg4Options { Devices = [Device("east", 0, port: "/dev/hvo/eg4-east"), duplicate] };

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
        var noPort = Device("no-port", 0);
        noPort.Port = null!;
        var result = new Eg4OptionsValidator(Environment("Production"))
            .Validate(null, new Eg4Options { Devices = [null!, noPort] });

        result.Failures.Should().Contain(message => message.Contains("null entries", StringComparison.Ordinal));
        result.Failures.Should().Contain(message => message.Contains("stable /dev/hvo HID", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validate_6500ExRequiresSingleOwnerPi30EndpointWithoutUnitAddress()
    {
        var valid = Device("valid", 0, port: "/dev/hvo/eg4-6500ex-a");
        var invalidUnit = Device("unit", 1, port: "/dev/hvo/eg4-6500ex-b");

        new Eg4OptionsValidator(Environment("Production"))
            .Validate(null, new Eg4Options { Devices = [valid] }).Succeeded.Should().BeTrue();
        var result = new Eg4OptionsValidator(Environment("Production"))
            .Validate(null, new Eg4Options { Devices = [invalidUnit] });
        result.Failures.Should().ContainSingle(message => message.Contains("PI30 does not use Modbus", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validate_MpptRequiresValidatedStablePathAndUnitOne()
    {
        var controller = Device("controller", 2, Eg4DeviceType.ChargeControllerMppt10048Hv);

        var result = new Eg4OptionsValidator(Environment("Production"))
            .Validate(null, new Eg4Options { Devices = [controller] });

        result.Failures.Should().ContainSingle(message => message.Contains("must be 1", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validate_RejectsSourceAndDeviceIdsLongerThanIngestContract()
    {
        var device = Device("length", 0, port: "/dev/hvo/eg4-length");
        device.SourceId = new string('s', 65);
        device.DeviceId = new string('d', 65);

        var result = new Eg4OptionsValidator(Environment("Production"))
            .Validate(null, new Eg4Options { Devices = [device] });

        result.Failures.Should().Contain(message => message.Contains("SourceId of 1-64", StringComparison.Ordinal));
        result.Failures.Should().Contain(message => message.Contains("DeviceId of 1-64", StringComparison.Ordinal));
    }

    private static Eg4DeviceOptions Device(
        string alias,
        byte unitId,
        Eg4DeviceType type = Eg4DeviceType.Inverter6500Ex,
        string port = "/dev/serial/by-id/usb-eg4-bus") => new()
    {
        Type = type,
        SourceId = $"eg4-{alias}",
        DeviceId = $"inverter-{alias}",
        Alias = alias,
        Port = port,
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
