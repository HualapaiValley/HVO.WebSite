using System.Globalization;
using System.Text.Json;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Contracts.Weather;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Exporter.HomeAssistant;

internal sealed class HomeAssistantStateProjector(IOptions<HomeAssistantExporterOptions> options)
{
    private readonly HomeAssistantExporterOptions options = options.Value;
    private readonly Dictionary<string, HomeAssistantState> states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> signatures = new(StringComparer.Ordinal);

    public IReadOnlyList<HomeAssistantMappedObservation> Reconcile(IEnumerable<HomeAssistantState> snapshot)
    {
        foreach (var entityId in options.Mappings.SelectMany(static mapping => mapping.Entities).Select(static binding => binding.EntityId!))
            states.Remove(entityId);
        foreach (var state in snapshot.Where(state => IsMapped(state.EntityId)))
            states[state.EntityId] = state;
        return options.Mappings.Select(TryMap).Where(static item => item is not null).Cast<HomeAssistantMappedObservation>().ToArray();
    }

    public HomeAssistantMappedObservation? Apply(HomeAssistantState state)
    {
        var mapping = options.Mappings.FirstOrDefault(mapping => mapping.Entities.Any(binding => binding.EntityId == state.EntityId));
        if (mapping is null)
            return null;
        if (states.TryGetValue(state.EntityId, out var current) && state.LastUpdatedUtc < current.LastUpdatedUtc)
            return null;
        states[state.EntityId] = state;
        return TryMap(mapping);
    }

    public void Acknowledge(HomeAssistantMappedObservation observation) =>
        signatures[observation.MappingId] = observation.Signature;

    private bool IsMapped(string entityId) => options.Mappings.Any(mapping => mapping.Entities.Any(binding => binding.EntityId == entityId));

    private HomeAssistantMappedObservation? TryMap(HomeAssistantExportMapping mapping)
    {
        var values = new Dictionary<HomeAssistantMetric, double>();
        var timestamp = DateTimeOffset.MinValue;
        foreach (var binding in mapping.Entities)
        {
            if (!states.TryGetValue(binding.EntityId!, out var state)
                || !TryNormalize(state, binding.Metric, out var value))
            {
                if (binding.Required)
                {
                    signatures.Remove(mapping.Id!);
                    return null;
                }
                continue;
            }
            values[binding.Metric] = value;
            if (state.LastUpdatedUtc > timestamp)
                timestamp = state.LastUpdatedUtc;
        }

        var signature = string.Join('|', values.OrderBy(static pair => pair.Key).Select(static pair => $"{pair.Key}:{pair.Value:R}"));
        if (signatures.TryGetValue(mapping.Id!, out var previous) && previous == signature)
            return null;
        object payload = mapping.Contract switch
        {
            HomeAssistantExportContract.PowerReading => new PowerReadingPayload
            {
                SourceId = mapping.SourceId,
                SourceSystem = "homeassistant-tplink",
                DeviceId = mapping.DeviceId,
                RecordedAtUtc = timestamp.UtcDateTime,
                LoadPowerW = values[HomeAssistantMetric.LoadPowerW],
                GridVoltageV = values.TryGetValue(HomeAssistantMetric.GridVoltageV, out var voltage) ? voltage : null
            },
            HomeAssistantExportContract.WeatherRaw => new WeatherRawPayload
            {
                StationId = mapping.SourceId!,
                RecordedAt = timestamp.UtcDateTime,
                TemperatureF = values[HomeAssistantMetric.Temperature],
                HumidityPercent = values[HomeAssistantMetric.HumidityPercent]
            },
            _ => throw new InvalidOperationException("Unsupported Home Assistant export contract.")
        };
        return new(mapping.Id!, signature, mapping.SourceId!, mapping.DeviceId!, timestamp, mapping.Contract, payload);
    }

    private static bool TryNormalize(HomeAssistantState state, HomeAssistantMetric metric, out double value)
    {
        value = default;
        if (state.State is "unknown" or "unavailable"
            || !double.TryParse(state.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || !double.IsFinite(parsed))
            return false;
        var unit = state.Attributes.TryGetProperty("unit_of_measurement", out var unitElement) ? unitElement.GetString() : null;
        var deviceClass = state.Attributes.TryGetProperty("device_class", out var classElement) ? classElement.GetString() : null;
        switch (metric)
        {
            case HomeAssistantMetric.LoadPowerW when deviceClass == "power" && unit == "W": value = parsed; return true;
            case HomeAssistantMetric.LoadPowerW when deviceClass == "power" && unit == "kW": value = parsed * 1000; return true;
            case HomeAssistantMetric.GridVoltageV when deviceClass == "voltage" && unit == "V": value = parsed; return true;
            case HomeAssistantMetric.GridVoltageV when deviceClass == "voltage" && unit == "mV": value = parsed / 1000; return true;
            case HomeAssistantMetric.Temperature when deviceClass == "temperature" && unit == "°F": value = parsed; return true;
            case HomeAssistantMetric.Temperature when deviceClass == "temperature" && unit == "°C": value = (parsed * 9 / 5) + 32; return true;
            case HomeAssistantMetric.HumidityPercent when deviceClass == "humidity" && unit == "%" && parsed is >= 0 and <= 100: value = parsed; return true;
            default: return false;
        }
    }
}
