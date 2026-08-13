using System.Text.Json;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Protocol.Packets;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.HomeAssistant;

public sealed class JkBmsHomeAssistantProjection
{
    private const string Measurement = "measurement";
    private readonly IHomeAssistantMqttProjection projection;
    private readonly EdgeRuntimeIdentity identity;
    private readonly string siteId;
    private readonly Lock definitionLock = new();
    private readonly Dictionary<string, int> bankNumbers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceMetadata> deviceMetadata = new(StringComparer.OrdinalIgnoreCase);

    public JkBmsHomeAssistantProjection(
        IHomeAssistantMqttProjection projection,
        EdgeRuntimeIdentity identity,
        IOptions<JkBmsOptions> options)
    {
        this.projection = projection;
        this.identity = identity;
        siteId = identity.SiteId
            ?? throw new InvalidOperationException("Edge:Runtime:SiteId is required for JK BMS Home Assistant identity.");
        var enabledDevices = (options.Value.Devices ?? [])
            .Where(static device => device.Enabled)
            .OrderBy(static device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (enabledDevices.Any(static device => string.IsNullOrWhiteSpace(device.DeviceId) || device.DeviceId != device.DeviceId.Trim()))
            throw new InvalidOperationException("Every enabled JkBms:Devices[].DeviceId must be non-empty and trimmed.");
        if (enabledDevices.Select(static device => device.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != enabledDevices.Length)
            throw new InvalidOperationException("Enabled JkBms:Devices[].DeviceId values must be unique ignoring case.");
        for (var index = 0; index < enabledDevices.Length; index++)
        {
            var device = enabledDevices[index];
            bankNumbers.Add(device.DeviceId, index + 1);
            EnsureDefinition(device, null, null);
        }
    }

    public bool Publish(BmsDeviceConfig device, BmsDeviceReading reading, DeviceInfoPacket? deviceInfo = null)
    {
        EnsureDefinition(device, reading, deviceInfo);
        var voltage = reading.TotalVoltageMv / 1000d;
        var current = reading.CurrentMa / 1000d;
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["battery_voltage"] = JsonSerializer.SerializeToElement(voltage),
            // JK protocol current is positive while charging and negative while discharging.
            ["battery_net_current"] = JsonSerializer.SerializeToElement(current),
            ["battery_net_power"] = JsonSerializer.SerializeToElement(voltage * current),
            ["state_of_charge"] = JsonSerializer.SerializeToElement(reading.StateOfChargePercent),
            ["battery_temperature_1"] = JsonSerializer.SerializeToElement(reading.BatteryTemperature1C),
            ["battery_temperature_2"] = JsonSerializer.SerializeToElement(reading.BatteryTemperature2C),
            ["mosfet_temperature"] = JsonSerializer.SerializeToElement(reading.PowerTubeTemperatureC),
            ["cell_count"] = JsonSerializer.SerializeToElement(reading.CellCount),
            ["average_cell_voltage"] = JsonSerializer.SerializeToElement(reading.AverageCellVoltageMv / 1000d),
            ["cell_delta"] = JsonSerializer.SerializeToElement(reading.DeltaCellVoltageMv),
            ["max_voltage_cell"] = JsonSerializer.SerializeToElement(reading.MaxVoltageCellIndex),
            ["min_voltage_cell"] = JsonSerializer.SerializeToElement(reading.MinVoltageCellIndex),
            ["balancing_current"] = JsonSerializer.SerializeToElement(reading.BalancingCurrentMa / 1000d),
            ["balancing"] = JsonSerializer.SerializeToElement(reading.BalancingActive),
            ["charging"] = JsonSerializer.SerializeToElement(reading.CurrentMa > 0),
            ["discharging"] = JsonSerializer.SerializeToElement(reading.CurrentMa < 0),
            ["alarm"] = JsonSerializer.SerializeToElement(reading.HasAlarms),
            ["alarm_bitmask"] = JsonSerializer.SerializeToElement(reading.AlarmBitmask),
            ["remaining_capacity"] = JsonSerializer.SerializeToElement(reading.RemainingCapacityMah / 1000d),
            ["nominal_capacity"] = JsonSerializer.SerializeToElement(reading.NominalCapacityMah / 1000d),
            ["state_of_health"] = JsonSerializer.SerializeToElement(reading.StateOfHealthPercent),
            ["cycle_count"] = JsonSerializer.SerializeToElement(reading.CycleCount),
            ["cycle_capacity"] = JsonSerializer.SerializeToElement(reading.CycleCapacityMah / 1000d),
        };
        if (reading.CellVoltagesMv.Count > 0)
        {
            values["max_cell_voltage"] = JsonSerializer.SerializeToElement(reading.CellVoltagesMv.Max() / 1000d);
            values["min_cell_voltage"] = JsonSerializer.SerializeToElement(reading.CellVoltagesMv.Min() / 1000d);
        }
        return projection.PublishCurrentState(new(
            Key(device),
            new DateTimeOffset(reading.RecordedAtUtc),
            values,
            available: true));
    }

    public bool PublishUnavailable(BmsDeviceConfig device, DateTime observedAtUtc) =>
        projection.PublishCurrentState(new(
            Key(device),
            new DateTimeOffset(observedAtUtc),
            Array.Empty<KeyValuePair<string, JsonElement>>(),
            available: false));

    private void EnsureDefinition(BmsDeviceConfig device, BmsDeviceReading? reading, DeviceInfoPacket? deviceInfo)
    {
        var metadata = Metadata(device, reading, deviceInfo);
        lock (definitionLock)
        {
            if (deviceMetadata.TryGetValue(device.DeviceId, out var existing) && existing == metadata)
                return;
            projection.UpsertDevice(CreateDefinition(device, metadata));
            deviceMetadata[device.DeviceId] = metadata;
        }
    }

    private DeviceMetadata Metadata(BmsDeviceConfig device, BmsDeviceReading? reading, DeviceInfoPacket? deviceInfo)
    {
        var bankNumber = bankNumbers[device.DeviceId];
        var model = Normalize(deviceInfo?.ManufacturerName);
        var hardwareVersion = Normalize(deviceInfo?.HardwareName);
        var softwareVersion = Normalize(deviceInfo?.FirmwareVersion);
        var capacityAh = reading is not null && reading.NominalCapacityMah > 0
            ? reading.NominalCapacityMah / 1000d
            : (double?)null;
        var name = model is null
            ? $"JK BMS Bank {bankNumber}"
            : capacityAh is null
                ? $"{model} - Bank {bankNumber}"
                : $"{model} {capacityAh.Value:0.##} Ah - Bank {bankNumber}";
        return new(name, model ?? "JK BMS", hardwareVersion, softwareVersion);
    }

    private HomeAssistantDeviceDefinition CreateDefinition(BmsDeviceConfig device, DeviceMetadata metadata) => new(
        Key(device),
        metadata.Name,
        Entities(),
        manufacturer: "Jikong",
        model: metadata.Model,
        softwareVersion: metadata.SoftwareVersion,
        hardwareVersion: metadata.HardwareVersion);

    private static IEnumerable<HomeAssistantEntityDefinition> Entities()
    {
        var entities = new List<HomeAssistantEntityDefinition>
        {
            new HomeAssistantSensorDefinition("battery_voltage", "Battery voltage", "V", "voltage", Measurement, suggestedDisplayPrecision: 3),
            new HomeAssistantSensorDefinition("battery_net_current", "Battery net current", "A", "current", Measurement, suggestedDisplayPrecision: 3),
            new HomeAssistantSensorDefinition("battery_net_power", "Battery net power", "W", "power", Measurement, suggestedDisplayPrecision: 2),
            new HomeAssistantSensorDefinition("state_of_charge", "State of charge", "%", "battery", Measurement, suggestedDisplayPrecision: 0),
            new HomeAssistantSensorDefinition("battery_temperature_1", "Battery temperature 1", "°C", "temperature", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 1),
            new HomeAssistantSensorDefinition("battery_temperature_2", "Battery temperature 2", "°C", "temperature", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 1),
            new HomeAssistantSensorDefinition("mosfet_temperature", "MOSFET temperature", "°C", "temperature", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 1),
            new HomeAssistantSensorDefinition("cell_count", "Cell count", entityCategory: "diagnostic", suggestedDisplayPrecision: 0),
            new HomeAssistantSensorDefinition("average_cell_voltage", "Average cell voltage", "V", "voltage", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 3),
            new HomeAssistantSensorDefinition("max_cell_voltage", "Maximum cell voltage", "V", "voltage", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 3),
            new HomeAssistantSensorDefinition("max_voltage_cell", "Maximum-voltage cell", entityCategory: "diagnostic", suggestedDisplayPrecision: 0),
            new HomeAssistantSensorDefinition("min_cell_voltage", "Minimum cell voltage", "V", "voltage", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 3),
            new HomeAssistantSensorDefinition("min_voltage_cell", "Minimum-voltage cell", entityCategory: "diagnostic", suggestedDisplayPrecision: 0),
            new HomeAssistantSensorDefinition("cell_delta", "Cell voltage delta", "mV", "voltage", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 0),
            new HomeAssistantSensorDefinition("balancing_current", "Balancing current", "A", "current", Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 3),
            new HomeAssistantBinarySensorDefinition("balancing", "Balancing", icon: "mdi:scale-balance", entityCategory: "diagnostic"),
            new HomeAssistantBinarySensorDefinition("charging", "Charging", deviceClass: "battery_charging"),
            new HomeAssistantBinarySensorDefinition("discharging", "Discharging", icon: "mdi:battery-minus"),
            new HomeAssistantBinarySensorDefinition("alarm", "Alarm", deviceClass: "problem"),
            new HomeAssistantSensorDefinition("alarm_bitmask", "Alarm bitmask", entityCategory: "diagnostic", suggestedDisplayPrecision: 0),
            new HomeAssistantSensorDefinition("remaining_capacity", "Remaining capacity", "Ah", stateClass: Measurement, suggestedDisplayPrecision: 3),
            new HomeAssistantSensorDefinition("nominal_capacity", "Nominal capacity", "Ah", stateClass: Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 3),
            new HomeAssistantSensorDefinition("state_of_health", "State of health", "%", stateClass: Measurement, entityCategory: "diagnostic", suggestedDisplayPrecision: 0),
            new HomeAssistantSensorDefinition("cycle_count", "Cycle count", stateClass: "total_increasing", entityCategory: "diagnostic", suggestedDisplayPrecision: 0),
            new HomeAssistantSensorDefinition("cycle_capacity", "Cumulative cycle capacity", "Ah", stateClass: "total_increasing", entityCategory: "diagnostic", suggestedDisplayPrecision: 3),
        };
        return entities;
    }

    private HomeAssistantDeviceKey Key(BmsDeviceConfig device) =>
        new(siteId, identity.GatewayId, device.DeviceId);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record DeviceMetadata(
        string Name,
        string Model,
        string? HardwareVersion,
        string? SoftwareVersion);
}
