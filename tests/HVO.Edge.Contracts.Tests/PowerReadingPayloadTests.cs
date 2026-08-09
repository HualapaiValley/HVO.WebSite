using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;

namespace HVO.Edge.Contracts.Tests;

[TestClass]
public sealed class PowerReadingPayloadTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void LegacyV1Payload_RoundTripsWithExactPropertyShape()
    {
        const string json = """
            {
              "sourceId": "solarassistant-total",
              "sourceSystem": "solarassistant",
              "deviceId": "total",
              "recordedAtUtc": "2026-05-23T08:00:00Z",
              "pvPowerW": 1200,
              "loadPowerW": 900,
              "gridPowerW": -50,
              "batteryPowerW": -250,
              "systemPowerW": 1000,
              "batteryStateOfChargePercent": 82,
              "batteryVoltageV": 53.2,
              "batteryCurrentA": -4.7,
              "batteryCapacityKwh": 30.72,
              "gridVoltageV": 240,
              "gridFrequencyHz": 60,
              "outputVoltageV": 120,
              "outputFrequencyHz": 60,
              "loadPercentage": 23,
              "inverterMode": "Battery",
              "outputSourcePriority": "SBU",
              "chargerSourcePriority": "Solar first"
            }
            """;

        var payload = JsonSerializer.Deserialize<PowerReadingPayload>(json, WebJsonOptions);
        var roundTripJson = JsonSerializer.Serialize(payload, WebJsonOptions);
        using var document = JsonDocument.Parse(roundTripJson);

        payload.Should().NotBeNull();
        payload!.RecordedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        payload.BatteryPowerW.Should().Be(-250);
        payload.BatteryCurrentA.Should().Be(-4.7);
        document.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal(
            "sourceId", "sourceSystem", "deviceId", "recordedAtUtc", "pvPowerW", "loadPowerW",
            "gridPowerW", "batteryPowerW", "systemPowerW", "batteryStateOfChargePercent",
            "batteryVoltageV", "batteryCurrentA", "batteryCapacityKwh", "gridVoltageV",
            "gridFrequencyHz", "outputVoltageV", "outputFrequencyHz", "loadPercentage",
            "inverterMode", "outputSourcePriority", "chargerSourcePriority");
    }

    [TestMethod]
    public void OptionalFields_RemainNullAndAreSerializedForV1Compatibility()
    {
        var payload = new PowerReadingPayload
        {
            SourceId = "smartshunt-main",
            DeviceId = "smartshunt-lifepo4",
            RecordedAtUtc = new DateTime(2026, 5, 23, 8, 0, 0, DateTimeKind.Utc),
        };

        var json = JsonSerializer.Serialize(payload, WebJsonOptions);
        var result = JsonSerializer.Deserialize<PowerReadingPayload>(json, WebJsonOptions);
        using var document = JsonDocument.Parse(json);

        result.Should().NotBeNull();
        result!.PvPowerW.Should().BeNull();
        result.BatteryStateOfChargePercent.Should().BeNull();
        document.RootElement.GetProperty("sourceSystem").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("chargerSourcePriority").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.EnumerateObject().Should().HaveCount(21);
    }
}
