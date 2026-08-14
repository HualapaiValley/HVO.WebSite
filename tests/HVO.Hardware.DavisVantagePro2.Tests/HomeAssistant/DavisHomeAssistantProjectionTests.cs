using FluentAssertions;
using HVO.Astronomy;
using HVO.Edge.Contracts;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.HomeAssistant;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station.Models;
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

        projection.Publish(CompleteReading(at), new StationSettings
        {
            LatitudeDegrees = 35.2,
            LongitudeDegrees = -113.8,
            GmtOffsetHours = -7
        });
        projection.PublishUnavailable(new DateTimeOffset(at.AddMinutes(1)));

        mqtt.Definition!.Key.Should().Be(new HomeAssistantDeviceKey("hvo", "davis", "station-1"));
        mqtt.Definition.Entities.Should().HaveCount(43);
        mqtt.Definition.Entities.Select(entity => entity.DefaultEntityId).Should().OnlyHaveUniqueItems();
        mqtt.Definition.Entities.Should().OnlyContain(entity => entity.DefaultEntityId == $"sensor.davis_{entity.ComponentId}");
        mqtt.Definition.Entities.Select(entity => HomeAssistantMqttIdentity.EntityUniqueId(mqtt.Definition.Key, entity.ComponentId))
            .Should().OnlyHaveUniqueItems();
        mqtt.States[0].Available.Should().BeTrue();
        mqtt.States[0].ComponentValues.Keys.Should().BeEquivalentTo(
            mqtt.Definition.Entities.Select(entity => entity.ComponentId));
        mqtt.States[0].ComponentValues["moon_phase"].GetString().Should().NotBeNullOrWhiteSpace();
        mqtt.States[0].ComponentValues["moon_illumination"].GetDouble().Should().BeInRange(0, 100);
        mqtt.States[0].ComponentValues["moonrise"].GetString().Should().NotBeNullOrWhiteSpace();
        mqtt.States[0].ComponentValues["moonset"].GetString().Should().NotBeNullOrWhiteSpace();
        mqtt.Definition.Entities.Single(entity => entity.ComponentId == "inside_temperature").EnabledByDefault.Should().BeTrue();
        mqtt.Definition.Entities.Single(entity => entity.ComponentId == "raw_pressure").EnabledByDefault.Should().BeFalse();
        Sensor(mqtt, "outside_temperature").SuggestedDisplayPrecision.Should().Be(1);
        Sensor(mqtt, "barometric_pressure").SuggestedDisplayPrecision.Should().Be(3);
        Sensor(mqtt, "wind_speed").SuggestedDisplayPrecision.Should().Be(0);
        Sensor(mqtt, "wind_speed_10_min_average").SuggestedDisplayPrecision.Should().Be(1);
        Sensor(mqtt, "daily_rain").SuggestedDisplayPrecision.Should().Be(3);
        Sensor(mqtt, "daily_et").SuggestedDisplayPrecision.Should().Be(3);
        Sensor(mqtt, "console_battery").SuggestedDisplayPrecision.Should().Be(3);
        mqtt.States[0].ComponentValues["transmitter_battery_status"].GetString().Should().Be("Low: 1, 3");
        mqtt.States[1].Available.Should().BeFalse();
    }

    private static HomeAssistantSensorDefinition Sensor(CaptureProjection mqtt, string componentId) =>
        mqtt.Definition!.Entities.OfType<HomeAssistantSensorDefinition>().Single(entity => entity.ComponentId == componentId);

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
    public void Projection_OmitsAstronomyWhenStationLocationIsUnavailable()
    {
        var mqtt = new CaptureProjection();
        var projection = new DavisHomeAssistantProjection(mqtt, Identity("hvo"), Options.Create(new StationOptions { StationId = "station-1" }));

        projection.Publish(new Loop2Packet { RecordedAtUtc = DateTime.UtcNow }, new StationSettings());

        mqtt.States.Single().ComponentValues.Should().NotContainKeys(
            "moon_phase", "moon_illumination", "moonrise", "moonset");
    }

    [TestMethod]
    public void Projection_CachesAstronomyByLocalDateLocationAndTimeZone()
    {
        var calls = new List<(DateTimeOffset ObservedLocal, TimeSpan Offset, double? Latitude, double? Longitude)>();
        var mqtt = new CaptureProjection();
        var projection = new DavisHomeAssistantProjection(
            mqtt,
            Identity("hvo"),
            Options.Create(new StationOptions { StationId = "station-1" }),
            (observedLocal, offset, latitude, longitude) =>
            {
                calls.Add((observedLocal, offset, latitude, longitude));
                return new MoonSnapshot(CelestialMarker.Hidden, "Full moon", 99, false, "99%", "8:01 PM", "5:42 AM");
            });
        var settings = new StationSettings
        {
            LatitudeDegrees = 35.7,
            LongitudeDegrees = -114.0,
            GmtOffsetHours = -7
        };

        projection.Publish(new Loop2Packet { RecordedAtUtc = new DateTime(2026, 5, 12, 12, 0, 0, DateTimeKind.Utc) }, settings);
        projection.Publish(new Loop2Packet { RecordedAtUtc = new DateTime(2026, 5, 12, 20, 0, 0, DateTimeKind.Utc) }, settings);
        projection.Publish(new Loop2Packet { RecordedAtUtc = new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc) }, settings);
        projection.Publish(new Loop2Packet { RecordedAtUtc = new DateTime(2026, 5, 13, 13, 0, 0, DateTimeKind.Utc) }, settings with { LongitudeDegrees = -113.9 });
        projection.Publish(new Loop2Packet { RecordedAtUtc = new DateTime(2026, 5, 13, 14, 0, 0, DateTimeKind.Utc) }, settings with { GmtOffsetHours = -6 });

        calls.Should().HaveCount(4);
        calls[0].ObservedLocal.Offset.Should().Be(TimeSpan.FromHours(-7));
        calls[1].ObservedLocal.Date.Should().Be(new DateTime(2026, 5, 13));
        calls[2].Longitude.Should().Be(-113.9);
        calls[3].Offset.Should().Be(TimeSpan.FromHours(-6));
    }

    [TestMethod]
    public void Projection_AstronomyMatchesKnownHualapaiValleyDateAndTimeZone()
    {
        var mqtt = new CaptureProjection();
        var projection = new DavisHomeAssistantProjection(mqtt, Identity("hvo"), Options.Create(new StationOptions { StationId = "station-1" }));
        var observedUtc = new DateTime(2026, 5, 12, 20, 0, 0, DateTimeKind.Utc);

        projection.Publish(new Loop2Packet { RecordedAtUtc = observedUtc }, new StationSettings
        {
            LatitudeDegrees = 35.7,
            LongitudeDegrees = -114.0,
            GmtOffsetHours = -7
        });

        var values = mqtt.States.Single().ComponentValues;
        values["moon_phase"].GetString().Should().Be("Waning crescent");
        values["moon_illumination"].GetDouble().Should().BeApproximately(20.9604, 0.001);
        values["moonrise"].GetString().Should().Be("2:54 AM");
        values["moonset"].GetString().Should().Be("3:04 PM");
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
