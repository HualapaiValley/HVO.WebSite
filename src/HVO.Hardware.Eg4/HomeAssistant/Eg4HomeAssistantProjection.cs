using System.Globalization;
using System.Text.Json;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Telemetry;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.HomeAssistant;

internal sealed class Eg4HomeAssistantProjection
{
    private const string Measurement = "measurement";
    private readonly IHomeAssistantMqttProjection projection;
    private readonly EdgeRuntimeIdentity identity;
    private readonly string siteId;

    public Eg4HomeAssistantProjection(
        IHomeAssistantMqttProjection projection,
        EdgeRuntimeIdentity identity,
        IOptions<Eg4Options> options)
    {
        this.projection = projection;
        this.identity = identity;
        siteId = identity.SiteId
            ?? throw new InvalidOperationException("Edge:Runtime:SiteId is required for EG4 Home Assistant identity.");
        foreach (var device in (options.Value.Devices ?? []).Where(static device => device.Enabled))
            projection.UpsertDevice(CreateDefinition(device));
    }

    public bool Publish(Eg4DeviceOptions device, Eg4TelemetrySample sample)
    {
        var observation = sample.BatteryObservation ?? throw new ArgumentException("An available EG4 sample requires a battery observation.", nameof(sample));
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        Add(values, "battery_voltage", observation.VoltageV);
        Add(values, "battery_net_current", observation.CurrentA);
        Add(values, "battery_net_power", observation.PowerW);
        Add(values, "battery_state_of_charge", observation.StateOfChargePercent);
        Add(values, "pv_power", TotalPvPower(device, sample));
        AddTracker(values, "pv_mppt_1", sample.MpptDetail?.Trackers.FirstOrDefault(static tracker => tracker.TrackerId == "mppt-1"));
        Add(values, "load_power", sample.InverterDetail?.Load?.LoadPowerW);
        Add(values, "temperature", sample.InverterDetail?.TemperatureC
            ?? sample.MpptDetail?.Temperatures.FirstOrDefault(static temperature => temperature.TemperatureC.HasValue)?.TemperatureC);
        Add(values, "operating_mode", sample.InverterDetail?.Operating?.Mode);
        if (device.Type == Eg4DeviceType.Inverter6500Ex)
            AddInverterDetail(values, sample);
        else
            AddControllerDetail(values, sample.MpptDetail);
        return projection.PublishCurrentState(new(
            Key(device),
            new DateTimeOffset(observation.ObservedAtUtc),
            values,
            available: true));
    }

    public bool PublishUnavailable(Eg4DeviceOptions device, DateTime observedAtUtc) =>
        projection.PublishCurrentState(new(
            Key(device),
            new DateTimeOffset(observedAtUtc),
            Array.Empty<KeyValuePair<string, JsonElement>>(),
            available: false));

    private HomeAssistantDeviceDefinition CreateDefinition(Eg4DeviceOptions device)
    {
        var isInverter = device.Type == Eg4DeviceType.Inverter6500Ex;
        var entities = new List<HomeAssistantEntityDefinition>
        {
            new HomeAssistantSensorDefinition("battery_voltage", "Battery voltage", "V", "voltage", Measurement, suggestedDisplayPrecision: isInverter ? 2 : 1),
            new HomeAssistantSensorDefinition("battery_net_current", "Battery net current", "A", "current", Measurement, suggestedDisplayPrecision: isInverter ? 0 : 1),
            new HomeAssistantSensorDefinition("battery_net_power", "Battery net power", "W", "power", Measurement, suggestedDisplayPrecision: 2),
            new HomeAssistantSensorDefinition("battery_state_of_charge", "Inverter-reported battery state of charge", "%", "battery", Measurement, entityCategory: "diagnostic", enabledByDefault: false, suggestedDisplayPrecision: 0),
            new HomeAssistantSensorDefinition("pv_power", "PV power", "W", "power", Measurement, suggestedDisplayPrecision: 0),
            new HomeAssistantSensorDefinition("pv_mppt_1_voltage", "PV MPPT 1 voltage", "V", "voltage", Measurement, suggestedDisplayPrecision: 1),
            new HomeAssistantSensorDefinition("pv_mppt_1_current", "PV MPPT 1 current", "A", "current", Measurement, suggestedDisplayPrecision: 1),
            new HomeAssistantSensorDefinition("pv_mppt_1_power", "PV MPPT 1 power", "W", "power", Measurement, suggestedDisplayPrecision: 0),
            new HomeAssistantSensorDefinition("temperature", "Temperature", "°C", "temperature", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 0),
        };
        if (device.Type == Eg4DeviceType.Inverter6500Ex)
        {
            entities.Add(new HomeAssistantSensorDefinition("pv_mppt_2_voltage", "PV MPPT 2 voltage", "V", "voltage", Measurement, suggestedDisplayPrecision: 1));
            entities.Add(new HomeAssistantSensorDefinition("pv_mppt_2_current", "PV MPPT 2 current", "A", "current", Measurement, suggestedDisplayPrecision: 1));
            entities.Add(new HomeAssistantSensorDefinition("pv_mppt_2_power", "PV MPPT 2 power", "W", "power", Measurement, suggestedDisplayPrecision: 0));
            entities.Add(new HomeAssistantSensorDefinition("ac_input_voltage", "AC input voltage", "V", "voltage", Measurement, suggestedDisplayPrecision: 1));
            entities.Add(new HomeAssistantSensorDefinition("ac_input_frequency", "AC input frequency", "Hz", "frequency", Measurement, suggestedDisplayPrecision: 1));
            entities.Add(new HomeAssistantSensorDefinition("ac_output_voltage", "AC output voltage", "V", "voltage", Measurement, suggestedDisplayPrecision: 1));
            entities.Add(new HomeAssistantSensorDefinition("ac_output_frequency", "AC output frequency", "Hz", "frequency", Measurement, suggestedDisplayPrecision: 1));
            entities.Add(new HomeAssistantSensorDefinition("load_power", "Load power", "W", "power", Measurement, suggestedDisplayPrecision: 0));
            entities.Add(new HomeAssistantSensorDefinition("load_apparent_power", "Load apparent power", "VA", "apparent_power", Measurement, suggestedDisplayPrecision: 0));
            entities.Add(new HomeAssistantSensorDefinition("load_percentage", "Load percentage", "%", stateClass: Measurement, suggestedDisplayPrecision: 0));
            entities.Add(new HomeAssistantSensorDefinition("operating_mode", "Operating mode", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("fault_code", "Fault code", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("status_flags", "Status flags", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("scc_pwm_temperature", "SCC PWM temperature", "°C", "temperature", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 0));
            entities.Add(new HomeAssistantSensorDefinition("inverter_temperature", "Inverter temperature", "°C", "temperature", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 0));
            entities.Add(new HomeAssistantSensorDefinition("battery_channel_temperature", "Battery channel temperature", "°C", "temperature", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 0));
            entities.Add(new HomeAssistantSensorDefinition("transformer_temperature", "Transformer temperature", "°C", "temperature", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 0));
            entities.Add(new HomeAssistantSensorDefinition("main_firmware", "Main firmware", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("secondary_firmware", "Secondary firmware", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("charge_stage", "Charge stage", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantBinarySensorDefinition("fan_locked", "Fan locked", deviceClass: "problem", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("fan_pwm_percentage", "Fan PWM", "%", stateClass: Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 0));
            entities.Add(new HomeAssistantSensorDefinition("parallel_role", "Parallel role", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("parallel_warning_flags", "Parallel warning flags", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("output_mode", "Output mode", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("charger_source_priority", "Charger source priority", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("parallel_total_load_power", "Parallel total load power", "W", "power", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 0));
            entities.Add(new HomeAssistantSensorDefinition("parallel_total_load_apparent_power", "Parallel total load apparent power", "VA", "apparent_power", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 0));
            entities.Add(new HomeAssistantSensorDefinition("parallel_total_load_percentage", "Parallel total load percentage", "%", stateClass: Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 0));
        }
        else
        {
            entities.Add(new HomeAssistantSensorDefinition("controller_temperature", "Controller temperature", "°C", "temperature", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 0));
            entities.Add(new HomeAssistantSensorDefinition("controller_secondary_temperature", "Controller secondary temperature", "°C", "temperature", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 0));
            entities.Add(new HomeAssistantSensorDefinition("controller_status", "Controller status", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("controller_diagnostic_201", "Controller diagnostic 201", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("charge_state", "Charge state", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("controller_estimated_soc", "Controller estimated state of charge", "%", stateClass: Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 0));
            entities.Add(new HomeAssistantSensorDefinition("controller_diagnostic_206", "Controller diagnostic 206", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("controller_diagnostic_212", "Controller diagnostic 212", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("controller_diagnostic_215", "Controller diagnostic 215", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("controller_diagnostic_216", "Controller diagnostic 216", entityCategory: "diagnostic"));
            entities.Add(new HomeAssistantSensorDefinition("controller_diagnostic_217", "Controller diagnostic 217", entityCategory: "diagnostic"));
        }
        return new(
            Key(device),
            device.Alias,
            entities,
            manufacturer: "EG4 Electronics",
            model: device.Type == Eg4DeviceType.Inverter6500Ex ? "6500EX" : "MPPT100-48HV");
    }

    private HomeAssistantDeviceKey Key(Eg4DeviceOptions device) =>
        new(siteId, identity.GatewayId, device.DeviceId);

    private static void Add(Dictionary<string, JsonElement> values, string componentId, double? value)
    {
        if (value.HasValue)
            values[componentId] = JsonSerializer.SerializeToElement(value.Value);
    }

    private static void Add(Dictionary<string, JsonElement> values, string componentId, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values[componentId] = JsonSerializer.SerializeToElement(value);
    }

    private static void Add(Dictionary<string, JsonElement> values, string componentId, bool? value)
    {
        if (value.HasValue)
            values[componentId] = JsonSerializer.SerializeToElement(value.Value);
    }

    private static void AddTracker(
        Dictionary<string, JsonElement> values,
        string componentPrefix,
        PowerMpptTrackerDetail? tracker)
    {
        Add(values, $"{componentPrefix}_voltage", tracker?.VoltageV);
        Add(values, $"{componentPrefix}_current", tracker?.CurrentA);
        Add(values, $"{componentPrefix}_power", tracker?.PowerW);
    }

    private static void AddInverterDetail(Dictionary<string, JsonElement> values, Eg4TelemetrySample sample)
    {
        var detail = sample.InverterDetail;
        AddTracker(values, "pv_mppt_2", sample.MpptDetail?.Trackers.FirstOrDefault(static tracker => tracker.TrackerId == "mppt-2"));
        Add(values, "ac_input_voltage", detail?.Ac?.InputVoltageV);
        Add(values, "ac_input_frequency", detail?.Ac?.InputFrequencyHz);
        Add(values, "ac_output_voltage", detail?.Ac?.OutputVoltageV);
        Add(values, "ac_output_frequency", detail?.Ac?.OutputFrequencyHz);
        Add(values, "load_apparent_power", detail?.Load?.LoadApparentPowerVa);
        Add(values, "load_percentage", detail?.Operating?.LoadPercentage);
        Add(values, "fault_code", detail?.Operating?.FaultCode);
        Add(values, "status_flags", detail?.Operating?.StatusFlags);
        AddTemperature(values, "scc_pwm_temperature", detail, "scc-pwm");
        AddTemperature(values, "inverter_temperature", detail, "inverter");
        AddTemperature(values, "battery_channel_temperature", detail, "battery-channel");
        AddTemperature(values, "transformer_temperature", detail, "transformer");
        Add(values, "main_firmware", Status(detail, "main-firmware"));
        Add(values, "secondary_firmware", Status(detail, "secondary-firmware"));
        Add(values, "charge_stage", Status(detail, "charge-stage"));
        Add(values, "fan_locked", ParseBoolean(Status(detail, "fan-locked")));
        Add(values, "fan_pwm_percentage", ParseDouble(Status(detail, "fan-pwm-percent")));
        Add(values, "parallel_role", Status(detail, "parallel-role"));
        Add(values, "parallel_warning_flags", Status(detail, "parallel-warning-flags"));
        Add(values, "output_mode", Status(detail, "output-mode"));
        Add(values, "charger_source_priority", Status(detail, "charger-source-priority"));
        Add(values, "parallel_total_load_power", ParseDouble(Status(detail, "parallel-total-load-active-w")));
        Add(values, "parallel_total_load_apparent_power", ParseDouble(Status(detail, "parallel-total-load-apparent-va")));
        Add(values, "parallel_total_load_percentage", ParseDouble(Status(detail, "parallel-total-load-percent")));
    }

    private static void AddControllerDetail(
        Dictionary<string, JsonElement> values,
        PowerMpptDetailPayload? detail)
    {
        AddControllerTemperature(values, "controller_temperature", detail, "controller");
        AddControllerTemperature(values, "controller_secondary_temperature", detail, "controller-secondary");
        Add(values, "controller_status", Diagnostic(detail, "r200"));
        Add(values, "controller_diagnostic_201", Diagnostic(detail, "r201"));
        Add(values, "charge_state", Diagnostic(detail, "r204"));
        Add(values, "controller_estimated_soc", ParseDouble(Diagnostic(detail, "controllerEstimatedSocPercent")));
        Add(values, "controller_diagnostic_206", Diagnostic(detail, "r206"));
        Add(values, "controller_diagnostic_212", Diagnostic(detail, "r212"));
        Add(values, "controller_diagnostic_215", Diagnostic(detail, "r215"));
        Add(values, "controller_diagnostic_216", Diagnostic(detail, "r216"));
        Add(values, "controller_diagnostic_217", Diagnostic(detail, "r217"));
    }

    private static void AddTemperature(
        Dictionary<string, JsonElement> values,
        string componentId,
        PowerInverterDetailPayload? detail,
        string temperatureId) =>
        Add(values, componentId, detail?.Temperatures.FirstOrDefault(
            temperature => string.Equals(temperature.TemperatureId, temperatureId, StringComparison.OrdinalIgnoreCase))?.TemperatureC);

    private static void AddControllerTemperature(
        Dictionary<string, JsonElement> values,
        string componentId,
        PowerMpptDetailPayload? detail,
        string temperatureId) =>
        Add(values, componentId, detail?.Temperatures.FirstOrDefault(
            temperature => string.Equals(temperature.TemperatureId, temperatureId, StringComparison.OrdinalIgnoreCase))?.TemperatureC);

    private static string? Status(PowerInverterDetailPayload? detail, string key) =>
        detail?.Statuses.FirstOrDefault(status => string.Equals(status.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string? Diagnostic(PowerMpptDetailPayload? detail, string key) =>
        detail?.Diagnostics.FirstOrDefault(diagnostic => string.Equals(diagnostic.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static bool? ParseBoolean(string? value) =>
        bool.TryParse(value, out var parsed) ? parsed : null;

    private static double? TotalPvPower(Eg4DeviceOptions device, Eg4TelemetrySample sample)
    {
        var trackers = sample.MpptDetail?.Trackers ?? [];
        var expectedCount = device.Type == Eg4DeviceType.Inverter6500Ex ? 2 : 1;
        return trackers.Count == expectedCount && trackers.All(static tracker => tracker.PowerW.HasValue)
            ? trackers.Sum(static tracker => tracker.PowerW!.Value)
            : null;
    }
}
