using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Configuration;

public sealed class Eg4OptionsValidator(IHostEnvironment environment) : IValidateOptions<Eg4Options>
{
    private const string StablePortPrefix = "/dev/serial/by-id/";

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
        var endpoints = new HashSet<(string Port, byte UnitId)>();

        foreach (var device in devices)
        {
            if (device is null) { failures.Add("Eg4:Devices cannot contain null entries."); continue; }
            ValidateIdentity(device.SourceId, "SourceId", sourceIds, failures);
            ValidateIdentity(device.DeviceId, "DeviceId", deviceIds, failures);
            ValidateIdentity(device.Alias, "Alias", aliases, failures);

            if (!Enum.IsDefined(device.Type) || device.Type == Eg4DeviceType.Unknown)
                failures.Add($"Device '{device.Alias}' has an unsupported Type.");
            if (string.IsNullOrWhiteSpace(device.Port) || device.Port != device.Port.Trim() || !device.Port.StartsWith(StablePortPrefix, StringComparison.Ordinal) ||
                device.Port.Length == StablePortPrefix.Length || device.Port.EndsWith('/') ||
                device.Port.Contains("..", StringComparison.Ordinal))
                failures.Add($"Device '{device.Alias}' Port must be a stable /dev/serial/by-id path.");
            if (device.UnitId is < 1 or > 247)
                failures.Add($"Device '{device.Alias}' UnitId must be between 1 and 247.");
            if (device.PollIntervalSeconds is < 10 or > 3600)
                failures.Add($"Device '{device.Alias}' PollIntervalSeconds must be between 10 and 3600.");
            if (!endpoints.Add((device.Port, device.UnitId)))
                failures.Add($"Port/unit combination '{device.Port}'/{device.UnitId} is configured more than once.");
        }
    }

    private static void ValidateIdentity(string value, string field, HashSet<string> values, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > 128)
            failures.Add($"Every EG4 device must have a trimmed {field} of 1-128 characters.");
        else if (!values.Add(value))
            failures.Add($"EG4 device {field} '{value}' must be unique.");
    }
}
