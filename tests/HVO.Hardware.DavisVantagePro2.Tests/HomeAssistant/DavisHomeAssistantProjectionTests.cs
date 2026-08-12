using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.HomeAssistant;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Tests.HomeAssistant;

[TestClass]
public sealed class DavisHomeAssistantProjectionTests
{
    [TestMethod]
    public void Projection_UsesStableIdentityCompleteScalarLiveStateAndAvailability()
    {
        var mqtt = new CaptureProjection();
        var projection = new DavisHomeAssistantProjection(mqtt, Identity("hvo"), Options.Create(new StationOptions { StationId = "station-1" }));
        var at = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

        projection.Publish(CompleteReading(at));
        projection.PublishUnavailable(new DateTimeOffset(at.AddMinutes(1)));

        mqtt.Definition!.Key.Should().Be(new HomeAssistantDeviceKey("hvo", "davis", "station-1"));
        mqtt.Definition.Entities.Should().HaveCount(39);
        mqtt.States[0].Available.Should().BeTrue();
        mqtt.States[0].ComponentValues.Keys.Should().BeEquivalentTo(
            mqtt.Definition.Entities.Select(entity => entity.ComponentId));
        mqtt.Definition.Entities.Single(entity => entity.ComponentId == "inside_temperature").EnabledByDefault.Should().BeTrue();
        mqtt.Definition.Entities.Single(entity => entity.ComponentId == "raw_pressure").EnabledByDefault.Should().BeFalse();
        mqtt.States[0].ComponentValues["transmitter_battery_status"].GetString().Should().Be("Low: 1, 3");
        mqtt.States[1].Available.Should().BeFalse();
    }

    [TestMethod]
    public void Projection_RequiresExplicitSiteId()
    {
        var action = () => new DavisHomeAssistantProjection(new CaptureProjection(), Identity(null), Options.Create(new StationOptions { StationId = "station-1" }));
        action.Should().Throw<InvalidOperationException>().WithMessage("*SiteId*");
    }

    [TestMethod]
    public void Projection_OmitsTransmitterBatteryStatusWhenTelemetryIsUnknown()
    {
        var mqtt = new CaptureProjection();
        var projection = new DavisHomeAssistantProjection(mqtt, Identity("hvo"), Options.Create(new StationOptions { StationId = "station-1" }));

        projection.Publish(new Loop2Packet { RecordedAtUtc = DateTime.UtcNow });

        mqtt.States.Single().ComponentValues.Should().NotContainKey("transmitter_battery_status");
        mqtt.States.Single().ComponentValues.Should().NotContainKey("transmitter_battery_bitmask");
    }

    [TestMethod]
    public void Projection_ReportsHealthyTransmitterBatteryOnlyForKnownZeroBitmask()
    {
        var mqtt = new CaptureProjection();
        var projection = new DavisHomeAssistantProjection(mqtt, Identity("hvo"), Options.Create(new StationOptions { StationId = "station-1" }));

        projection.Publish(new Loop2Packet { RecordedAtUtc = DateTime.UtcNow, TransmitterBatteryStatus = 0 });

        mqtt.States.Single().ComponentValues["transmitter_battery_status"].GetString().Should().Be("OK");
    }

    private static EdgeRuntimeIdentity Identity(string? siteId) => new(
        "hvo-davis", "1", "instance", "davis", "davis-vantage-pro2", GatewayDomain.Weather,
        "station-1", siteId, "station-1", "Testing", "host", "Davis");

    private static Loop2Packet CompleteReading(DateTime at) => new()
    {
        RecordedAtUtc = at,
        OutsideTemperatureF = 72,
        InsideTemperatureF = 75,
        DewPointF = 39,
        HeatIndexF = 73,
        WindChillF = 70,
        ThswF = 74,
        OutsideHumidityPercent = 30,
        InsideHumidityPercent = 25,
        BarometricPressureInHg = 29.92,
        PressureRawInHg = 27.1,
        AltimeterInHg = 30.02,
        BarometricTrend = 20,
        WindSpeedMph = 5,
        WindDirectionDegrees = 180,
        WindSpeed10MinAvgMph = 4,
        WindSpeed2MinAvgMph = 5,
        WindGust10MinMph = 9,
        WindGust10MinDirectionDegrees = 190,
        RainRateInchesPerHour = 0.1,
        DailyRainInches = 0.2,
        Rain15MinInches = 0.01,
        HourRainInches = 0.03,
        Rain24HourInches = 0.2,
        StormRainInches = 0.4,
        StormStartDate = new DateTime(2026, 8, 11),
        MonthlyRainInches = 1.2,
        YearlyRainInches = 4.5,
        SolarRadiationWm2 = 800,
        UvIndex = 7.2,
        DailyEtInches = 0.02,
        MonthlyEtInches = 0.4,
        YearlyEtInches = 3.1,
        ConsoleBatteryVoltage = 5.5,
        TransmitterBatteryStatus = 5,
        ForecastRule = 6,
        SunriseTime = 538,
        SunsetTime = 1932,
    };

    private sealed class CaptureProjection : IHomeAssistantMqttProjection
    {
        public HomeAssistantDeviceDefinition? Definition { get; private set; }
        public List<HomeAssistantCurrentState> States { get; } = [];
        public void UpsertDevice(HomeAssistantDeviceDefinition definition) => Definition = definition;
        public bool PublishCurrentState(HomeAssistantCurrentState state) { States.Add(state); return true; }
        public bool RemoveDevice(HomeAssistantDeviceKey key) => false;
        public HomeAssistantMqttStatus GetStatus() => new(true, true, Definition is null ? 0 : 1, null, null, null);
    }
}
