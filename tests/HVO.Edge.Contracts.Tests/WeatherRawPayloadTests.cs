using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts.Weather;

namespace HVO.Edge.Contracts.Tests;

[TestClass]
public sealed class WeatherRawPayloadTests
{
    [TestMethod]
    public void JsonShape_MatchesCanonicalWeatherIngestContract()
    {
        var payload = new WeatherRawPayload
        {
            StationId = "govee:sensor-1",
            RecordedAt = DateTime.Parse("2026-08-11T10:00:00Z").ToUniversalTime(),
            TemperatureF = 68,
            HumidityPercent = 40
        };

        var json = JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.GetProperty("stationId").GetString().Should().Be("govee:sensor-1");
        json.GetProperty("recordedAt").GetDateTime().Kind.Should().Be(DateTimeKind.Utc);
        json.GetProperty("temperatureF").GetDouble().Should().Be(68);
    }
}
