using FluentAssertions;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Protocol;
using HVO.Hardware.Eg4.Telemetry;

namespace HVO.Hardware.Eg4.Tests.Protocol;

[TestClass]
public sealed class Eg46500ExLiveTests
{
    [TestMethod]
    [TestCategory("Live")]
    public async Task ConfirmedHidrawEndpoint_ReturnsValidatedBatteryObservation()
    {
        var port = Environment.GetEnvironmentVariable("EG4_6500EX_HIDRAW_PATH");
        if (string.IsNullOrWhiteSpace(port))
            Assert.Inconclusive("Set EG4_6500EX_HIDRAW_PATH to an owner-confirmed 6500EX USB HID endpoint.");

        await using var source = new Eg46500ExTelemetrySource(
            new Eg46500ExHidrawTransportFactory(TimeProvider.System),
            TimeProvider.System);
        var observation = await source.ReadAsync(new Eg4DeviceOptions
        {
            Type = Eg4DeviceType.Inverter6500Ex,
            SourceId = "eg4-live-6500ex",
            DeviceId = "live-6500ex",
            Alias = "Live 6500EX",
            Port = port!,
            UnitId = 0,
        }, CancellationToken.None);

        observation.VoltageV.Should().BeInRange(40, 65);
        observation.StateOfChargePercent.Should().BeInRange(0, 100);
        observation.CurrentA.Should().NotBeNull();
        observation.PowerW.Should().NotBeNull();
    }
}
