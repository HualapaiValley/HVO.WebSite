using System.Net.NetworkInformation;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.Configuration;

public sealed class SmartShuntOptionsValidator : IValidateOptions<SmartShuntOptions>
{
    public ValidateOptionsResult Validate(string? name, SmartShuntOptions options)
    {
        var failures = new List<string>();
        if (!PhysicalAddress.TryParse(options.Address, out var address) || address.GetAddressBytes().Length != 6)
            failures.Add("SmartShunt:Address must be a six-byte Bluetooth MAC address.");
        if (string.IsNullOrWhiteSpace(options.Adapter) || !options.Adapter.StartsWith("hci", StringComparison.Ordinal)
            || options.Adapter.Length <= 3 || options.Adapter.AsSpan(3).IndexOfAnyExceptInRange('0', '9') >= 0)
            failures.Add("SmartShunt:Adapter must use the BlueZ hci<number> format.");
        if (!Uri.TryCreate(options.CentralIngestBaseEndpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
            failures.Add("SmartShunt:CentralIngestBaseEndpoint must be an absolute HTTP(S) URI without credentials, query, or fragment.");
        else if (endpoint.Scheme != "https" && !options.AllowInsecureCentralIngest)
            failures.Add("SmartShunt:CentralIngestBaseEndpoint must use HTTPS unless insecure ingest is explicitly enabled for testing.");
        if (string.IsNullOrWhiteSpace(options.CentralApiKeySecret))
            failures.Add("SmartShunt:CentralApiKeySecret is required.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
