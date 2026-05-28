using System.Globalization;
using System.Text.Json;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Gateway.SolarAssistant.Configuration;

namespace HVO.Gateway.SolarAssistant.SolarAssistant;

public static class SolarAssistantEnergyInverterDetailMapper
{
    public static PowerEnergyPayload MapEnergy(
        IReadOnlyList<SolarAssistantMetric> metrics,
        SolarAssistantOptions options,
        DateTime recordedAtUtc,
        PowerEnergyPayload? previous = null)
    {
        var byTopic = ToTopicDictionary(metrics);
        var counters = new[]
        {
            Counter(byTopic, "pv_energy", "PV energy", "total/pv_energy"),
            Counter(byTopic, "load_energy", "Load energy", "total/load_energy"),
            Counter(byTopic, "grid_energy_in", "Grid import energy", "total/grid_energy_in"),
            Counter(byTopic, "grid_energy_out", "Grid export energy", "total/grid_energy_out"),
            Counter(byTopic, "battery_energy_in", "Battery charge energy", "total/battery_energy_in"),
            Counter(byTopic, "battery_energy_out", "Battery discharge energy", "total/battery_energy_out"),
        }
        .Where(c => c is not null)
        .Cast<PowerEnergyCounter>()
        .ToArray();

        return new PowerEnergyPayload
        {
            SourceId = options.TotalSourceId,
            SourceSystem = "solarassistant",
            DeviceId = options.TotalDeviceId,
            RecordedAtUtc = recordedAtUtc.ToUniversalTime(),
            CounterResetDetected = HasCounterReset(counters, previous),
            Counters = counters,
        };
    }

    public static PowerInverterDetailPayload MapInverterDetail(
        IReadOnlyList<SolarAssistantMetric> metrics,
        SolarAssistantOptions options,
        DateTime recordedAtUtc)
    {
        var byTopic = ToTopicDictionary(metrics);
        var pvStrings = new[]
        {
            PvString(byTopic, "1"),
            PvString(byTopic, "2"),
        }.Where(s => s is not null).Cast<PowerPvStringDetail>().ToArray();

        var load = new PowerInverterLoadDetail
        {
            LoadPowerW = ReadDouble(byTopic, "inverter_1/load_power"),
            LoadApparentPowerVa = ReadDouble(byTopic, "inverter_1/load_apparent_power"),
            SystemAndLoadPowerW = ReadDouble(byTopic, "inverter_1/system_and_load_power"),
        };
        var battery = new PowerInverterBatteryDetail
        {
            VoltageV = ReadDouble(byTopic, "inverter_1/battery_voltage"),
            CurrentA = ReadDouble(byTopic, "inverter_1/battery_current"),
            PowerW = ReadDouble(byTopic, "inverter_1/battery_power"),
        };

        return new PowerInverterDetailPayload
        {
            SourceId = options.TotalSourceId,
            SourceSystem = "solarassistant",
            DeviceId = "inverter_1",
            RecordedAtUtc = recordedAtUtc.ToUniversalTime(),
            PvStrings = pvStrings,
            Load = HasAnyValue(load) ? load : null,
            Battery = HasAnyValue(battery) ? battery : null,
            TemperatureC = ReadDouble(byTopic, "inverter_1/temperature"),
            Statuses = Statuses(byTopic),
        };
    }

    private static IReadOnlyDictionary<string, SolarAssistantMetric> ToTopicDictionary(IReadOnlyList<SolarAssistantMetric> metrics) => metrics
        .Where(m => !string.IsNullOrWhiteSpace(m.Topic))
        .GroupBy(m => NormalizeTopic(m.Topic), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

    private static PowerEnergyCounter? Counter(IReadOnlyDictionary<string, SolarAssistantMetric> byTopic, string key, string name, string topic)
    {
        var value = ReadDouble(byTopic, topic);
        if (value is null)
            return null;

        return new PowerEnergyCounter
        {
            Key = key,
            Name = name,
            ValueKwh = value.Value,
            DeviceId = "total",
            SourceTopic = topic,
        };
    }

    private static bool HasCounterReset(IReadOnlyList<PowerEnergyCounter> counters, PowerEnergyPayload? previous)
    {
        if (previous is null || counters.Count == 0 || previous.Counters.Count == 0)
            return false;

        var prior = previous.Counters.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
        return counters.Any(c => prior.TryGetValue(c.Key, out var p) && c.ValueKwh < p.ValueKwh);
    }

    private static PowerPvStringDetail? PvString(IReadOnlyDictionary<string, SolarAssistantMetric> byTopic, string id)
    {
        var detail = new PowerPvStringDetail
        {
            StringId = id,
            PowerW = ReadDouble(byTopic, $"inverter_1/pv_power_{id}"),
            VoltageV = ReadDouble(byTopic, $"inverter_1/pv_voltage_{id}"),
            CurrentA = ReadDouble(byTopic, $"inverter_1/pv_current_{id}"),
        };

        return detail.PowerW is null && detail.VoltageV is null && detail.CurrentA is null ? null : detail;
    }

    private static IReadOnlyList<PowerInverterStatusDetail> Statuses(IReadOnlyDictionary<string, SolarAssistantMetric> byTopic) =>
        byTopic
            .Where(kvp => kvp.Key.StartsWith("inverter_1/status_", StringComparison.OrdinalIgnoreCase))
            .Select(kvp => new PowerInverterStatusDetail
            {
                Key = kvp.Key.Replace('/', '.'),
                Value = ReadString(kvp.Value) ?? string.Empty,
                SourceTopic = kvp.Key,
            })
            .Where(s => !string.IsNullOrWhiteSpace(s.Value))
            .OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool HasAnyValue(PowerInverterLoadDetail detail) =>
        detail.LoadPowerW is not null || detail.LoadApparentPowerVa is not null || detail.SystemAndLoadPowerW is not null;

    private static bool HasAnyValue(PowerInverterBatteryDetail detail) =>
        detail.VoltageV is not null || detail.CurrentA is not null || detail.PowerW is not null;

    private static double? ReadDouble(IReadOnlyDictionary<string, SolarAssistantMetric> byTopic, string topic) =>
        byTopic.TryGetValue(topic, out var metric) ? ReadDouble(metric.Value) : null;

    private static double? ReadDouble(object? value) => value switch
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

    private static string? ReadString(SolarAssistantMetric metric) => metric.Value switch
    {
        null => null,
        JsonElement { ValueKind: JsonValueKind.String } e => NormalizeString(e.GetString()),
        JsonElement e => NormalizeString(e.ToString()),
        _ => NormalizeString(Convert.ToString(metric.Value, CultureInfo.InvariantCulture)),
    };

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static string NormalizeTopic(string topic) => topic.Trim().Trim('/');

    private static string? NormalizeString(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
