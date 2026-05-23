using System.Globalization;
using System.Text.Json;
using HVO.Gateway.SolarAssistant.Configuration;

namespace HVO.Gateway.SolarAssistant.SolarAssistant;

/// <summary>Maps SolarAssistant aggregate metrics into the website power ingest payload.</summary>
public static class SolarAssistantPowerMapper
{
    public static PowerReadingPayload MapTotalSnapshot(
        IReadOnlyList<SolarAssistantMetric> metrics,
        SolarAssistantOptions options,
        DateTime recordedAtUtc)
    {
        var byTopic = metrics
            .Where(m => !string.IsNullOrWhiteSpace(m.Topic))
            .GroupBy(m => NormalizeTopic(m.Topic), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        return new PowerReadingPayload
        {
            SourceId = options.TotalSourceId,
            SourceSystem = "solarassistant",
            DeviceId = options.TotalDeviceId,
            RecordedAtUtc = recordedAtUtc.ToUniversalTime(),
            PvPowerW = ReadDouble(byTopic, "total/pv_power"),
            LoadPowerW = ReadDouble(byTopic, "total/load_power"),
            GridPowerW = ReadDouble(byTopic, "total/grid_power"),
            BatteryPowerW = ReadDouble(byTopic, "total/battery_power"),
            SystemPowerW = ReadDouble(byTopic, "total/power"),
            BatteryStateOfChargePercent = ReadDouble(byTopic, "total/battery_state_of_charge"),
            BatteryVoltageV = ReadDouble(byTopic, "battery_1/voltage"),
            BatteryCurrentA = ReadDouble(byTopic, "battery_1/current"),
            BatteryCapacityKwh = ReadDouble(byTopic, "battery_1/capacity"),
            GridVoltageV = ReadDouble(byTopic, "inverter_1/grid_voltage"),
            GridFrequencyHz = ReadDouble(byTopic, "inverter_1/grid_frequency"),
            OutputVoltageV = ReadDouble(byTopic, "inverter_1/output_voltage"),
            OutputFrequencyHz = ReadDouble(byTopic, "inverter_1/output_frequency"),
            LoadPercentage = ReadDouble(byTopic, "inverter_1/load_percentage"),
            InverterMode = ReadString(byTopic, "inverter_1/device_mode"),
            OutputSourcePriority = ReadString(byTopic, "inverter_1/output_source_priority"),
            ChargerSourcePriority = ReadString(byTopic, "inverter_1/charger_source_priority"),
        };
    }

    private static string NormalizeTopic(string topic) => topic.Trim().Trim('/');

    private static double? ReadDouble(
        IReadOnlyDictionary<string, SolarAssistantMetric> byTopic,
        string topic)
    {
        if (!byTopic.TryGetValue(topic, out var metric))
            return null;

        return metric.Value switch
        {
            null => null,
            double d => d,
            float f => f,
            decimal d => (double)d,
            int i => i,
            long l => l,
            JsonElement { ValueKind: JsonValueKind.Number } e when e.TryGetDouble(out var d) => d,
            JsonElement { ValueKind: JsonValueKind.String } e => ParseDouble(e.GetString()),
            string s => ParseDouble(s),
            _ => null,
        };
    }

    private static string? ReadString(
        IReadOnlyDictionary<string, SolarAssistantMetric> byTopic,
        string topic)
    {
        if (!byTopic.TryGetValue(topic, out var metric))
            return null;

        return metric.Value switch
        {
            null => null,
            JsonElement { ValueKind: JsonValueKind.String } e => NormalizeString(e.GetString()),
            JsonElement e => NormalizeString(e.ToString()),
            _ => NormalizeString(Convert.ToString(metric.Value, CultureInfo.InvariantCulture)),
        };
    }

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static string? NormalizeString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
