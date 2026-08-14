using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Configuration;

public sealed class WeatherUndergroundOptions
{
    internal static readonly TimeSpan RetryBackoff = TimeSpan.FromMilliseconds(250);
    public const string SectionName = "WeatherUnderground";

    public bool Enabled { get; set; }
    public string StationId { get; set; } = "KAZKINGM12";

    [Range(5, 300)]
    public int IntervalSeconds { get; set; } = 5;

    [Range(1, 30)]
    public int RequestTimeoutSeconds { get; set; } = 2;

    public string StationKeySecret { get; set; } = "weather-underground-station-key";

    internal TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);
    internal TimeSpan RequestTimeout => TimeSpan.FromSeconds(RequestTimeoutSeconds);
    internal TimeSpan StaleAfter => TimeSpan.FromTicks(Interval.Ticks * 2);
}

internal sealed class WeatherUndergroundOptionsValidator : IValidateOptions<WeatherUndergroundOptions>
{
    public ValidateOptionsResult Validate(string? name, WeatherUndergroundOptions options)
    {
        if (!options.Enabled)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.StationId)
            || options.StationId != options.StationId.Trim()
            || options.StationId.Length > 32
            || options.StationId.Any(static character => !char.IsAsciiLetterOrDigit(character)))
            failures.Add("WeatherUnderground:StationId must contain 1-32 ASCII letters or digits.");
        if (string.IsNullOrWhiteSpace(options.StationKeySecret)
            || options.StationKeySecret != options.StationKeySecret.Trim()
            || Path.IsPathFullyQualified(options.StationKeySecret)
            || !string.Equals(Path.GetFileName(options.StationKeySecret), options.StationKeySecret, StringComparison.Ordinal))
            failures.Add("WeatherUnderground:StationKeySecret must be a file name relative to the secrets directory.");
        if (TimeSpan.FromSeconds(options.RequestTimeoutSeconds * 2) + WeatherUndergroundOptions.RetryBackoff
            >= TimeSpan.FromSeconds(options.IntervalSeconds))
            failures.Add("WeatherUnderground:RequestTimeoutSeconds must allow two bounded attempts within the publish interval.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
