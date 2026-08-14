using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Configuration;

namespace HVO.Hardware.DavisVantagePro2.Tests.Configuration;

[TestClass]
public sealed class CwopOptionsValidatorTests
{
    private readonly CwopOptionsValidator validator = new();

    [TestMethod]
    public void Validate_Disabled_AllowsUnconfiguredOptions()
    {
        validator.Validate(null, new CwopOptions()).Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public void Validate_EnabledValidConfiguration_Succeeds()
    {
        validator.Validate(null, Valid()).Succeeded.Should().BeTrue();
    }

    [TestMethod]
    [DataRow(299)]
    [DataRow(3601)]
    public void Validate_EnabledOutOfRangeCadence_Fails(int intervalSeconds)
    {
        var options = Valid();
        options.IntervalSeconds = intervalSeconds;
        options.StaleAfterSeconds = Math.Max(intervalSeconds, 600);

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain("Cwop:IntervalSeconds must be between 300 and 3600 seconds.");
    }

    [TestMethod]
    public void Validate_EnabledUnsafeOrInvalidConfiguration_Fails()
    {
        var options = Valid();
        options.StationId = "DW 4515";
        options.SoftwareName = "HVO Davis";
        options.Passcode = string.Empty;
        options.PasscodeSecret = "/run/secrets/passcode";
        options.StaleAfterSeconds = 60;

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(4);
    }

    private static CwopOptions Valid() => new()
    {
        Enabled = true,
        StationId = "DW4515",
        Host = "cwop.aprs.net",
        IntervalSeconds = 300,
        StaleAfterSeconds = 600,
        Passcode = "-1",
        SoftwareName = "HVO-Davis",
        SoftwareVersion = "1.0",
    };
}
