using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Configuration;

public sealed class Eg4OptionsValidator(IHostEnvironment environment) : IValidateOptions<Eg4Options>
{
    private const string StablePortPrefix = "/dev/serial/by-id/";
    private const string StableHidrawPrefix = "/dev/hvo/";

    public ValidateOptionsResult Validate(string? name, Eg4Options options)
    {
        var failures = new List<string>();
        if (options.DefaultPollIntervalSeconds is < 10 or > 3600)
            failures.Add("Eg4:DefaultPollIntervalSeconds must be between 10 and 3600.");

        if (options.SimulationEnabled && !environment.IsEnvironment("Testing") && !environment.IsDevelopment())
            failures.Add($"EG4 simulation is not allowed in the {environment.EnvironmentName} environment.");

        ValidateDevices(options.Devices ?? [], failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateDevices(IReadOnlyList<Eg4DeviceOptions> devices, List<string> failures)
    {
        var sourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var endpoints = new HashSet<string>(StringComparer.Ordinal);

        foreach (var device in devices)
        {
            if (device is null) { failures.Add("Eg4:Devices cannot contain null entries."); continue; }
            ValidateIdentity(device.SourceId, "SourceId", 64, sourceIds, failures);
            ValidateIdentity(device.DeviceId, "DeviceId", 64, deviceIds, failures);
            ValidateIdentity(device.Alias, "Alias", 128, aliases, failures);

            if (!Enum.IsDefined(device.Type) || device.Type == Eg4DeviceType.Unknown)
                failures.Add($"Device '{device.Alias}' has an unsupported Type.");
            var stableSerialPort = IsStablePath(device.Port, StablePortPrefix);
            var stableHidrawPort = IsStablePath(device.Port, StableHidrawPrefix);
            if (device.Type == Eg4DeviceType.Inverter6500Ex)
            {
                if (!stableHidrawPort)
                    failures.Add($"Device '{device.Alias}' Port must be a stable /dev/hvo HID path.");
                if (device.UnitId != 0)
                    failures.Add($"Device '{device.Alias}' UnitId must be 0 because PI30 does not use Modbus addressing.");
                if (!endpoints.Add($"pi30:{device.Port}"))
                    failures.Add($"PI30 port '{device.Port}' is configured more than once.");
            }
            else
            {
                if (!stableSerialPort)
                    failures.Add($"Device '{device.Alias}' Port must be a stable /dev/serial/by-id path.");
                if (device.UnitId != 1)
                    failures.Add($"Device '{device.Alias}' UnitId must be 1 for the validated MPPT100-48HV mapping.");
                if (!endpoints.Add($"modbus:{device.Port}:{device.UnitId}"))
                    failures.Add($"Port/unit combination '{device.Port}'/{device.UnitId} is configured more than once.");
            }
            if (device.PollIntervalSeconds is < 10 or > 3600)
                failures.Add($"Device '{device.Alias}' PollIntervalSeconds must be between 10 and 3600.");
        }
    }

    private static bool IsStablePath(string? value, string prefix) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.StartsWith(prefix, StringComparison.Ordinal) &&
        value.Length > prefix.Length && !value.EndsWith('/') && !value.Contains("..", StringComparison.Ordinal);

    private static void ValidateIdentity(string value, string field, int maximumLength, HashSet<string> values, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > maximumLength)
            failures.Add($"Every EG4 device must have a trimmed {field} of 1-{maximumLength} characters.");
        else if (!values.Add(value))
            failures.Add($"EG4 device {field} '{value}' must be unique.");
    }
}
