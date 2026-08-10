using FluentAssertions;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Protocol;
using HVO.Hardware.Eg4.Telemetry;

namespace HVO.Hardware.Eg4.Tests.Protocol;

[TestClass]
public sealed class Eg4Mppt10048HvLiveTests
{
    [TestMethod]
    [TestCategory("Live")]
    public async Task StableSerialEndpoint_ReturnsValidatedFixedRegisterSample()
    {
        var port = Environment.GetEnvironmentVariable("EG4_MPPT10048HV_SERIAL_PATH");
        if (string.IsNullOrWhiteSpace(port))
            Assert.Inconclusive("Set EG4_MPPT10048HV_SERIAL_PATH to the controller's stable /dev/serial/by-id path.");

        await using var coordinator = new Eg4PortCoordinator(
            new Eg4Mppt10048HvSerialTransportFactory(TimeProvider.System));
        var source = new Eg4Mppt10048HvTelemetrySource(coordinator, TimeProvider.System);
        var sample = await source.ReadAsync(new Eg4DeviceOptions
        {
            Type = Eg4DeviceType.ChargeControllerMppt10048Hv,
            SourceId = "eg4-live-mppt",
            DeviceId = "live-mppt",
            Alias = "Live MPPT100-48HV",
            Port = port!,
            UnitId = 1,
        }, CancellationToken.None);

        sample.IsAvailable.Should().BeTrue();
        sample.BatteryObservation.Should().NotBeNull();
        sample.MpptDetail.Should().NotBeNull();
    }
}
