using System.Net.NetworkInformation;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Configuration;

public sealed class JkBmsOptionsValidator : IValidateOptions<JkBmsOptions>
{
    public ValidateOptionsResult Validate(string? name, JkBmsOptions options)
    {
        var failures = new List<string>();
        if (!Uri.TryCreate(options.CentralIngestEndpoint, UriKind.Absolute, out var ingest)
            || ingest.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(ingest.UserInfo)
            || !string.IsNullOrEmpty(ingest.Query)
            || !string.IsNullOrEmpty(ingest.Fragment))
            failures.Add("JkBms:CentralIngestEndpoint must be an absolute HTTP(S) URI without credentials, query, or fragment.");
        else if (ingest.Scheme != "https" && !options.AllowInsecureCentralIngest)
            failures.Add("JkBms:CentralIngestEndpoint must use HTTPS unless insecure ingest is explicitly enabled for testing.");
        if (string.IsNullOrWhiteSpace(options.CentralApiKeySecret))
            failures.Add("JkBms:CentralApiKeySecret is required.");
        if (!IsAdapterName(options.HciAdapter))
            failures.Add("JkBms:HciAdapter must use the BlueZ hci<number> format.");

        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in options.Devices ?? [])
        {
            if (device is null)
            {
                failures.Add("JkBms:Devices cannot contain null entries.");
                continue;
            }

            if (!PhysicalAddress.TryParse(device.Address, out var address) || address.GetAddressBytes().Length != 6)
                failures.Add($"JK BMS device '{device.Alias}' Address must be a six-byte Bluetooth MAC address.");
            else if (!addresses.Add(device.Address))
                failures.Add($"JK BMS Address '{device.Address}' must be unique.");
            ValidateIdentity(device.Alias, "Alias", 128, aliases, failures);
            ValidateIdentity(device.DeviceId, "DeviceId", 64, deviceIds, failures);
            if (!string.IsNullOrWhiteSpace(device.HciAdapter) && !IsAdapterName(device.HciAdapter))
                failures.Add($"JK BMS device '{device.Alias}' HciAdapter must use the BlueZ hci<number> format.");
            if (device.PollIntervalSeconds is > 0 and < 10)
                failures.Add($"JK BMS device '{device.Alias}' PollIntervalSeconds must be zero or between 10 and 3600.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsAdapterName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.StartsWith("hci", StringComparison.Ordinal)
        && value.Length > 3
        && value.AsSpan(3).IndexOfAnyExceptInRange('0', '9') < 0;

    private static void ValidateIdentity(
        string value,
        string field,
        int maximumLength,
        HashSet<string> values,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > maximumLength)
            failures.Add($"Every JK BMS device must have a trimmed {field} of 1-{maximumLength} characters.");
        else if (!values.Add(value))
            failures.Add($"JK BMS device {field} '{value}' must be unique.");
    }
}
