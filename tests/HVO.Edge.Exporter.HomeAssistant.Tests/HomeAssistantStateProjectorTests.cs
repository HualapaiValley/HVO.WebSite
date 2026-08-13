using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Contracts.Weather;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Exporter.HomeAssistant.Tests;

[TestClass]
public sealed class HomeAssistantStateProjectorTests
{
    [TestMethod]
    public void Reconcile_MapsTypedPowerAndWeatherContractsWithUnitConversion()
    {
        var projector = new HomeAssistantStateProjector(Options.Create(TestOptions.Create()));
        var now = DateTimeOffset.Parse("2026-08-11T10:00:00Z");

        var observations = projector.Reconcile([
            State("sensor.kasa_power", "1.25", "kW", "power", now),
            State("sensor.kasa_voltage", "120000", "mV", "voltage", now.AddSeconds(1)),
            State("sensor.govee_temperature", "20", "°C", "temperature", now),
            State("sensor.govee_humidity", "40", "%", "humidity", now.AddSeconds(2))
        ]);

        observations.Should().HaveCount(2);
        var power = observations.Single(item => item.Contract == HomeAssistantExportContract.PowerReading);
        power.Payload.Should().BeOfType<PowerReadingPayload>().Which.Should().Match<PowerReadingPayload>(payload =>
            payload.LoadPowerW == 1250 && payload.GridVoltageV == 120 && payload.SourceSystem == "homeassistant-tplink");
        var weather = observations.Single(item => item.Contract == HomeAssistantExportContract.WeatherRaw);
        weather.Payload.Should().BeOfType<WeatherRawPayload>().Which.Should().Match<WeatherRawPayload>(payload =>
            payload.TemperatureF == 68 && payload.HumidityPercent == 40);
    }

    [TestMethod]
    public void Apply_SuppressesUnchangedAndOlderStates()
    {
        var projector = new HomeAssistantStateProjector(Options.Create(TestOptions.PowerOnly()));
        var now = DateTimeOffset.Parse("2026-08-11T10:00:00Z");
        var initial = projector.Reconcile([State("sensor.kasa_power", "100", "W", "power", now)]).Should().ContainSingle().Which;
        projector.Acknowledge(initial);

        projector.Apply(State("sensor.kasa_power", "100", "W", "power", now.AddSeconds(1))).Should().BeNull();
        projector.Apply(State("sensor.kasa_power", "90", "W", "power", now.AddSeconds(-1))).Should().BeNull();
        projector.Apply(State("sensor.kasa_power", "90", "W", "power", now.AddSeconds(2))).Should().NotBeNull();
    }

    [TestMethod]
    public void Reconcile_SuppressesMissingOrUnavailableRequiredValues()
    {
        var projector = new HomeAssistantStateProjector(Options.Create(TestOptions.Create()));
        var now = DateTimeOffset.UtcNow;

        projector.Reconcile([
            State("sensor.kasa_power", "unavailable", "W", "power", now),
            State("sensor.govee_temperature", "20", "°C", "temperature", now)
        ]).Should().BeEmpty();
    }

    [TestMethod]
    public void Reconcile_RetriesUnacknowledgedObservationAndEmitsAfterSameValueRecovery()
    {
        var projector = new HomeAssistantStateProjector(Options.Create(TestOptions.PowerOnly()));
        var now = DateTimeOffset.Parse("2026-08-11T10:00:00Z");
        var available = State("sensor.kasa_power", "100", "W", "power", now);

        projector.Reconcile([available]).Should().ContainSingle();
        var persisted = projector.Reconcile([available]).Should().ContainSingle().Which;
        projector.Acknowledge(persisted);
        projector.Apply(State("sensor.kasa_power", "unavailable", "W", "power", now.AddSeconds(1))).Should().BeNull();
        projector.Apply(State("sensor.kasa_power", "100", "W", "power", now.AddSeconds(2))).Should().NotBeNull();
    }

    [TestMethod]
    public void Reconcile_RemovesStateMissingFromAuthoritativeSnapshot()
    {
        var projector = new HomeAssistantStateProjector(Options.Create(TestOptions.PowerOnly()));
        var now = DateTimeOffset.Parse("2026-08-11T10:00:00Z");
        var initial = projector.Reconcile([State("sensor.kasa_power", "100", "W", "power", now)]).Single();
        projector.Acknowledge(initial);

        projector.Reconcile([]).Should().BeEmpty();
        projector.Reconcile([State("sensor.kasa_power", "100", "W", "power", now)]).Should().ContainSingle();
    }

    private static HomeAssistantState State(string id, string value, string unit, string deviceClass, DateTimeOffset updated) => new(
        id,
        value,
        JsonSerializer.SerializeToElement(new { unit_of_measurement = unit, device_class = deviceClass }),
        updated);
}
