using System.Text.Json;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.HomeAssistant;

public interface IDavisHomeAssistantProjection
{
    bool Publish(Loop2Packet reading);
    bool PublishUnavailable(DateTimeOffset observedAtUtc);
}

public sealed class DavisHomeAssistantProjection : IDavisHomeAssistantProjection
{
    private const string Measurement = "measurement";
    private readonly IHomeAssistantMqttProjection projection;
    private readonly HomeAssistantDeviceKey key;

    public DavisHomeAssistantProjection(
        IHomeAssistantMqttProjection projection,
        EdgeRuntimeIdentity identity,
        IOptions<StationOptions> options)
    {
        this.projection = projection;
        var siteId = identity.SiteId
            ?? throw new InvalidOperationException("Edge:Runtime:SiteId is required for Davis Home Assistant identity.");
        key = new(siteId, identity.GatewayId, options.Value.StationId);
        projection.UpsertDevice(new(
            key,
            "Davis Vantage Pro 2",
            [
                new HomeAssistantSensorDefinition("outside_temperature", "Outside temperature", "°F", "temperature", Measurement),
                new HomeAssistantSensorDefinition("outside_humidity", "Outside humidity", "%", "humidity", Measurement),
                new HomeAssistantSensorDefinition("dew_point", "Dew point", "°F", "temperature", Measurement),
                new HomeAssistantSensorDefinition("barometric_pressure", "Barometric pressure", "inHg", "atmospheric_pressure", Measurement),
                new HomeAssistantSensorDefinition("wind_speed", "Wind speed", "mph", "wind_speed", Measurement),
                new HomeAssistantSensorDefinition("wind_direction", "Wind direction", "°", stateClass: Measurement),
                new HomeAssistantSensorDefinition("wind_gust", "10-minute wind gust", "mph", "wind_speed", Measurement),
                new HomeAssistantSensorDefinition("rain_rate", "Rain rate", "in/h", "precipitation_intensity", Measurement),
                new HomeAssistantSensorDefinition("daily_rain", "Daily rain", "in", "precipitation", "total_increasing"),
                new HomeAssistantSensorDefinition("solar_radiation", "Solar radiation", "W/m²", "irradiance", Measurement),
                new HomeAssistantSensorDefinition("uv_index", "UV index", stateClass: Measurement),
                new HomeAssistantSensorDefinition("console_battery", "Console battery", "V", "voltage", Measurement, entityCategory: "diagnostic"),
                new HomeAssistantSensorDefinition("forecast", "Forecast", entityCategory: "diagnostic"),
            ],
            manufacturer: "Davis Instruments",
            model: "Vantage Pro 2"));
    }

    public bool Publish(Loop2Packet reading)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        Add(values, "outside_temperature", reading.OutsideTemperatureF);
        Add(values, "outside_humidity", reading.OutsideHumidityPercent);
        Add(values, "dew_point", reading.DewPointF);
        Add(values, "barometric_pressure", reading.BarometricPressureInHg);
        Add(values, "wind_speed", reading.WindSpeedMph);
        Add(values, "wind_direction", reading.WindDirectionDegrees);
        Add(values, "wind_gust", reading.WindGust10MinMph);
        Add(values, "rain_rate", reading.RainRateInchesPerHour);
        Add(values, "daily_rain", reading.DailyRainInches);
        Add(values, "solar_radiation", reading.SolarRadiationWm2);
        Add(values, "uv_index", reading.UvIndex);
        Add(values, "console_battery", reading.ConsoleBatteryVoltage);
        Add(values, "forecast", reading.ForecastString);
        return projection.PublishCurrentState(new(key, new DateTimeOffset(reading.RecordedAtUtc), values, available: true));
    }

    public bool PublishUnavailable(DateTimeOffset observedAtUtc) =>
        projection.PublishCurrentState(new(key, observedAtUtc, Array.Empty<KeyValuePair<string, JsonElement>>(), available: false));

    private static void Add(IDictionary<string, JsonElement> values, string id, double? value)
    {
        if (value.HasValue) values[id] = JsonSerializer.SerializeToElement(value.Value);
    }

    private static void Add(IDictionary<string, JsonElement> values, string id, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) values[id] = JsonSerializer.SerializeToElement(value);
    }
}
