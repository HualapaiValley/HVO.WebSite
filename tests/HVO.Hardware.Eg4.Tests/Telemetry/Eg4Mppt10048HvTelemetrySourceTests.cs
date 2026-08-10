using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Protocol;
using HVO.Hardware.Eg4.Telemetry;

namespace HVO.Hardware.Eg4.Tests.Telemetry;

[TestClass]
public sealed class Eg4Mppt10048HvTelemetrySourceTests
{
    [TestMethod]
    public void DaylightFixture_DecodesCanonicalBatteryTrackerTemperaturesAndDiagnostics()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "mppt100-48hv", "daylight-2026-08-10.json")));
        var words = fixture.RootElement.GetProperty("registerWords").EnumerateArray()
            .Select(value => value.GetUInt16()).ToArray();
        var time = new DateTime(2026, 8, 10, 18, 0, 0, DateTimeKind.Utc);

        var sample = Eg4Mppt10048HvTelemetrySource.Decode(Device("a"), words, time);
        var observation = sample.BatteryObservation!;

        sample.IsAvailable.Should().BeTrue();
        observation.VoltageV.Should().Be(54.3);
        observation.CurrentA.Should().Be(-9.1);
        observation.PowerW.Should().BeApproximately(-494.13, 0.001);
        observation.StateOfChargePercent.Should().BeNull("register 205 is diagnostic controller-estimated SOC only");
        observation.Provenance.Should().Be(PowerObservationProvenance.Derived);
        sample.MpptDetail!.Trackers.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            TrackerId = "mppt-1", VoltageV = (double?)400.6, CurrentA = (double?)1.2, PowerW = (double?)480,
        });
        sample.MpptDetail.Temperatures.Select(value => value.TemperatureC).Should().Equal(40, 37);
        sample.MpptDetail.Diagnostics.Select(value => value.Key).Should().Equal(
            "r200", "r201", "r204", "controllerEstimatedSocPercent", "r206", "r212", "r215", "r216", "r217");
    }

    [TestMethod]
    public void Decode_RejectsUnvalidatedLayoutAndPreservesValidZeroPvValues()
    {
        var invalid = ValidWords();
        invalid[5] = ushort.MaxValue;
        var zeroPv = ValidWords();
        zeroPv[7] = 0;
        zeroPv[8] = 0;
        zeroPv[9] = 0;

        FluentActions.Invoking(() => Eg4Mppt10048HvTelemetrySource.Decode(Device("a"), invalid, DateTime.UtcNow))
            .Should().Throw<Eg4TransportException>().Which.Kind.Should().Be(Eg4TransportFailureKind.Protocol);
        var sample = Eg4Mppt10048HvTelemetrySource.Decode(Device("b"), zeroPv, DateTime.UtcNow);
        sample.MpptDetail!.Trackers.Single().VoltageV.Should().Be(0);
        sample.MpptDetail.Trackers.Single().CurrentA.Should().Be(0);
        sample.MpptDetail.Trackers.Single().PowerW.Should().Be(0);
    }

    [TestMethod]
    public async Task ReadAsync_UsesOnlyFixedRequestAndTimeoutReturnsUnavailableWithoutZeroObservation()
    {
        var coordinator = new FakeCoordinator(
            new Eg4TransportException(Eg4TransportFailureKind.Timeout, "silence"),
            new Eg4TransportException(Eg4TransportFailureKind.Timeout, "silence"));
        var source = new Eg4Mppt10048HvTelemetrySource(coordinator, TimeProvider.System);

        var sample = await source.ReadAsync(Device("a"), CancellationToken.None);

        sample.IsAvailable.Should().BeFalse();
        sample.BatteryObservation.Should().BeNull();
        coordinator.Requests.Should().OnlyContain(request => request == Eg4Mppt10048HvProtocol.Request);
    }

    [TestMethod]
    public async Task ReadAsync_PreservesConfiguredSourceAndDeviceIndependence()
    {
        var words = ValidWords();
        var coordinator = new FakeCoordinator(
            new Eg4ReadRegistersResponse(words), new Eg4ReadRegistersResponse(words));
        var source = new Eg4Mppt10048HvTelemetrySource(coordinator, TimeProvider.System);

        var first = await source.ReadAsync(Device("east"), CancellationToken.None);
        var second = await source.ReadAsync(Device("west"), CancellationToken.None);

        first.BatteryObservation!.SourceId.Should().Be("eg4-mppt-east");
        second.BatteryObservation!.SourceId.Should().Be("eg4-mppt-west");
        first.BatteryObservation.DeviceId.Should().NotBe(second.BatteryObservation.DeviceId);
    }

    private static Eg4DeviceOptions Device(string id) => new()
    {
        Type = Eg4DeviceType.ChargeControllerMppt10048Hv,
        SourceId = $"eg4-mppt-{id}",
        DeviceId = $"mppt-{id}",
        Alias = $"MPPT {id}",
        Port = $"/dev/serial/by-id/eg4-{id}",
        UnitId = 1,
    };

    private static ushort[] ValidWords() =>
        [2, 2, 543, 91, 0, 90, 4038, 4006, 12, 480, 40, 37, 40, 0, 0, 13, 91, 2];

    private sealed class FakeCoordinator(params object[] responses) : IEg4PortCoordinator
    {
        private readonly Queue<object> _responses = new(responses);
        public List<Eg4ReadRegistersRequest> Requests { get; } = [];
        public ValueTask<Eg4ReadRegistersResponse> ReadRegistersAsync(string port, Eg4ReadRegistersRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var response = _responses.Dequeue();
            if (response is Exception exception) throw exception;
            return ValueTask.FromResult((Eg4ReadRegistersResponse)response);
        }
    }
}
