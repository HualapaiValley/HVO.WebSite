using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Configuration;

public sealed class CwopOptions
{
    public const string SectionName = "Cwop";

    public bool Enabled { get; set; }
    public string StationId { get; set; } = string.Empty;
    public string Host { get; set; } = "cwop.aprs.net";

    [Range(1, 65535)]
    public int Port { get; set; } = 14580;

    [Range(300, 3600)]
    public int IntervalSeconds { get; set; } = 300;

    [Range(1, 30)]
    public int ConnectTimeoutSeconds { get; set; } = 5;

    [Range(1, 30)]
    public int OperationTimeoutSeconds { get; set; } = 5;

    [Range(60, 3600)]
    public int StaleAfterSeconds { get; set; } = 600;

    public string Passcode { get; set; } = "-1";
    public string? PasscodeSecret { get; set; }
    public string SoftwareName { get; set; } = "HVO-Davis";
    public string SoftwareVersion { get; set; } = "1.0";

    internal TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);
    internal TimeSpan ConnectTimeout => TimeSpan.FromSeconds(ConnectTimeoutSeconds);
    internal TimeSpan OperationTimeout => TimeSpan.FromSeconds(OperationTimeoutSeconds);
    internal TimeSpan StaleAfter => TimeSpan.FromSeconds(StaleAfterSeconds);
}

internal sealed class CwopOptionsValidator : IValidateOptions<CwopOptions>
{
    public ValidateOptionsResult Validate(string? name, CwopOptions options)
    {
        if (!options.Enabled)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.StationId)
            || options.StationId != options.StationId.Trim()
            || options.StationId.Length is < 3 or > 9
            || options.StationId.Any(static character => !char.IsAsciiLetterOrDigit(character)))
            failures.Add("Cwop:StationId must contain 3-9 ASCII letters or digits.");
        if (string.IsNullOrWhiteSpace(options.Host) || options.Host != options.Host.Trim())
            failures.Add("Cwop:Host is required and must not contain surrounding whitespace.");
        if (!ValidToken(options.SoftwareName) || !ValidToken(options.SoftwareVersion))
            failures.Add("Cwop software name and version must be non-empty ASCII tokens without whitespace.");
        if (options.IntervalSeconds is < 300 or > 3600)
            failures.Add("Cwop:IntervalSeconds must be between 300 and 3600 seconds.");
        if (string.IsNullOrWhiteSpace(options.PasscodeSecret) && !ValidPasscode(options.Passcode))
            failures.Add("Cwop:Passcode must be -1 or a numeric APRS-IS passcode.");
        if (!string.IsNullOrWhiteSpace(options.PasscodeSecret)
            && (Path.IsPathFullyQualified(options.PasscodeSecret)
                || !string.Equals(Path.GetFileName(options.PasscodeSecret), options.PasscodeSecret, StringComparison.Ordinal)))
            failures.Add("Cwop:PasscodeSecret must be a file name relative to the secrets directory.");
        if (options.StaleAfterSeconds < options.IntervalSeconds)
            failures.Add("Cwop:StaleAfterSeconds must be at least Cwop:IntervalSeconds.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool ValidToken(string value) => !string.IsNullOrWhiteSpace(value)
        && value.All(static character => character is >= '!' and <= '~' && !char.IsWhiteSpace(character));

    private static bool ValidPasscode(string value) => value == "-1"
        || (value.Length is >= 1 and <= 5
            && value.All(char.IsAsciiDigit)
            && int.TryParse(value, out var passcode)
            && passcode <= 32767);
}
