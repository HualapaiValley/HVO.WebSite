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
                Sensor("outside_temperature", "Outside temperature", "°F", "temperature", Measurement),
                Sensor("inside_temperature", "Inside temperature", "°F", "temperature", Measurement),
                Sensor("outside_humidity", "Outside humidity", "%", "humidity", Measurement),
                Sensor("inside_humidity", "Inside humidity", "%", "humidity", Measurement),
                Sensor("dew_point", "Dew point", "°F", "temperature", Measurement),
                Sensor("heat_index", "Heat index", "°F", "temperature", Measurement),
                Sensor("wind_chill", "Wind chill", "°F", "temperature", Measurement),
                Sensor("thsw_index", "THSW index", "°F", "temperature", Measurement, enabledByDefault: false),
                Sensor("barometric_pressure", "Barometric pressure", "inHg", "atmospheric_pressure", Measurement),
                Sensor("raw_pressure", "Raw station pressure", "inHg", "atmospheric_pressure", Measurement, entityCategory: "diagnostic", enabledByDefault: false),
                Sensor("altimeter_pressure", "Altimeter pressure", "inHg", "atmospheric_pressure", Measurement, entityCategory: "diagnostic", enabledByDefault: false),
                Sensor("barometric_trend", "Barometric trend", stateClass: Measurement, entityCategory: "diagnostic", enabledByDefault: false),
                Sensor("wind_speed", "Wind speed", "mph", "wind_speed", Measurement),
                Sensor("wind_direction", "Wind direction", "°", stateClass: Measurement),
                Sensor("wind_speed_10_min_average", "10-minute average wind speed", "mph", "wind_speed", Measurement),
                Sensor("wind_speed_2_min_average", "2-minute average wind speed", "mph", "wind_speed", Measurement, enabledByDefault: false),
                Sensor("wind_gust", "10-minute wind gust", "mph", "wind_speed", Measurement),
                Sensor("wind_gust_direction", "10-minute wind gust direction", "°", stateClass: Measurement),
                Sensor("rain_rate", "Rain rate", "in/h", "precipitation_intensity", Measurement),
                Sensor("daily_rain", "Daily rain", "in", "precipitation", "total_increasing"),
                Sensor("rain_15_min", "15-minute rain", "in", "precipitation", Measurement, enabledByDefault: false),
                Sensor("rain_1_hour", "1-hour rain", "in", "precipitation", Measurement, enabledByDefault: false),
                Sensor("rain_24_hour", "24-hour rain", "in", "precipitation", Measurement),
                Sensor("storm_rain", "Storm rain", "in", "precipitation", Measurement),
                Sensor("storm_start", "Storm start", entityCategory: "diagnostic", enabledByDefault: false),
                Sensor("monthly_rain", "Monthly rain", "in", "precipitation", "total_increasing"),
                Sensor("yearly_rain", "Yearly rain", "in", "precipitation", "total_increasing"),
                Sensor("solar_radiation", "Solar radiation", "W/m²", "irradiance", Measurement),
                Sensor("uv_index", "UV index", stateClass: Measurement),
                Sensor("daily_et", "Daily evapotranspiration", "in", stateClass: "total_increasing"),
                Sensor("monthly_et", "Monthly evapotranspiration", "in", stateClass: "total_increasing", enabledByDefault: false),
                Sensor("yearly_et", "Yearly evapotranspiration", "in", stateClass: "total_increasing", enabledByDefault: false),
                Sensor("console_battery", "Console battery", "V", "voltage", Measurement, entityCategory: "diagnostic"),
                Sensor("transmitter_battery_status", "Transmitter battery status", entityCategory: "diagnostic"),
                Sensor("transmitter_battery_bitmask", "Transmitter battery bitmask", stateClass: Measurement, entityCategory: "diagnostic", enabledByDefault: false),
                Sensor("forecast", "Forecast", entityCategory: "diagnostic"),
                Sensor("forecast_rule", "Forecast rule", stateClass: Measurement, entityCategory: "diagnostic", enabledByDefault: false),
                Sensor("sunrise", "Sunrise"),
                Sensor("sunset", "Sunset"),
            ],
            manufacturer: "Davis Instruments",
            model: "Vantage Pro 2"));
    }

    public bool Publish(Loop2Packet reading)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        Add(values, "outside_temperature", reading.OutsideTemperatureF);
        Add(values, "inside_temperature", reading.InsideTemperatureF);
        Add(values, "outside_humidity", reading.OutsideHumidityPercent);
        Add(values, "inside_humidity", reading.InsideHumidityPercent);
        Add(values, "dew_point", reading.DewPointF);
        Add(values, "heat_index", reading.HeatIndexF);
        Add(values, "wind_chill", reading.WindChillF);
        Add(values, "thsw_index", reading.ThswF);
        Add(values, "barometric_pressure", reading.BarometricPressureInHg);
        Add(values, "raw_pressure", reading.PressureRawInHg);
        Add(values, "altimeter_pressure", reading.AltimeterInHg);
        Add(values, "barometric_trend", reading.BarometricTrend);
        Add(values, "wind_speed", reading.WindSpeedMph);
        Add(values, "wind_direction", reading.WindDirectionDegrees);
        Add(values, "wind_speed_10_min_average", reading.WindSpeed10MinAvgMph);
        Add(values, "wind_speed_2_min_average", reading.WindSpeed2MinAvgMph);
        Add(values, "wind_gust", reading.WindGust10MinMph);
        Add(values, "wind_gust_direction", reading.WindGust10MinDirectionDegrees);
        Add(values, "rain_rate", reading.RainRateInchesPerHour);
        Add(values, "daily_rain", reading.DailyRainInches);
        Add(values, "rain_15_min", reading.Rain15MinInches);
        Add(values, "rain_1_hour", reading.HourRainInches);
        Add(values, "rain_24_hour", reading.Rain24HourInches);
        Add(values, "storm_rain", reading.StormRainInches);
        Add(values, "storm_start", reading.StormStartDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Add(values, "monthly_rain", reading.MonthlyRainInches);
        Add(values, "yearly_rain", reading.YearlyRainInches);
        Add(values, "solar_radiation", reading.SolarRadiationWm2);
        Add(values, "uv_index", reading.UvIndex);
        Add(values, "daily_et", reading.DailyEtInches);
        Add(values, "monthly_et", reading.MonthlyEtInches);
        Add(values, "yearly_et", reading.YearlyEtInches);
        Add(values, "console_battery", reading.ConsoleBatteryVoltage);
        if (reading.TransmitterBatteryStatus.HasValue)
        {
            Add(values, "transmitter_battery_status", reading.TransmitterLowBatteryChannels.Count == 0
                ? "OK"
                : $"Low: {string.Join(", ", reading.TransmitterLowBatteryChannels)}");
        }
        Add(values, "transmitter_battery_bitmask", reading.TransmitterBatteryStatus);
        Add(values, "forecast", reading.ForecastString);
        Add(values, "forecast_rule", reading.ForecastRule);
        Add(values, "sunrise", reading.SunriseDisplay);
        Add(values, "sunset", reading.SunsetDisplay);
        return projection.PublishCurrentState(new(key, new DateTimeOffset(reading.RecordedAtUtc), values, available: true));
    }

    public bool PublishUnavailable(DateTimeOffset observedAtUtc) =>
        projection.PublishCurrentState(new(key, observedAtUtc, Array.Empty<KeyValuePair<string, JsonElement>>(), available: false));

    private static HomeAssistantSensorDefinition Sensor(
        string componentId,
        string name,
        string? unitOfMeasurement = null,
        string? deviceClass = null,
        string? stateClass = null,
        string? icon = null,
        string? entityCategory = null,
        bool enabledByDefault = true) => new(
            componentId,
            name,
            unitOfMeasurement,
            deviceClass,
            stateClass,
            icon,
            entityCategory,
            enabledByDefault,
            $"sensor.davis_{componentId}");

    private static void Add(IDictionary<string, JsonElement> values, string id, double? value)
    {
        if (value.HasValue) values[id] = JsonSerializer.SerializeToElement(value.Value);
    }

    private static void Add(IDictionary<string, JsonElement> values, string id, int? value)
    {
        if (value.HasValue) values[id] = JsonSerializer.SerializeToElement(value.Value);
    }

    private static void Add(IDictionary<string, JsonElement> values, string id, ushort? value)
    {
        if (value.HasValue) values[id] = JsonSerializer.SerializeToElement(value.Value);
    }

    private static void Add(IDictionary<string, JsonElement> values, string id, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) values[id] = JsonSerializer.SerializeToElement(value);
    }
}
