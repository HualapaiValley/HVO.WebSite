using Microsoft.Extensions.Options;

namespace HVO.WebSite.v9.Configuration;

public sealed class PowerCompositionOptions
{
    public const string SectionName = "PowerComposition";
    public int SmartShuntFreshnessSeconds { get; set; } = 1800;
    public int SolarAssistantFreshnessSeconds { get; set; } = 1800;
    public int JkBmsFreshnessSeconds { get; set; } = 1800;
    public int Eg4BranchFreshnessSeconds { get; set; } = 180;
    public int MaxDerivationSkewSeconds { get; set; } = 30;
    public int MaxFutureClockSkewSeconds { get; set; } = 30;
    public List<string> PreferredSmartShuntSourceIds { get; set; } = [];
    public List<string> PreferredSolarAssistantSourceIds { get; set; } = [];
    public List<string> EnabledMpptSourceIds { get; set; } = [];
    public List<string> ExpectedPvTrackerIds { get; set; } = [];
}

public sealed class PowerCompositionOptionsValidator : IValidateOptions<PowerCompositionOptions>
{
    public ValidateOptionsResult Validate(string? name, PowerCompositionOptions options)
    {
        var failures = new List<string>();
        foreach (var (field, value) in new[]
        {
            (nameof(options.SmartShuntFreshnessSeconds), options.SmartShuntFreshnessSeconds),
            (nameof(options.SolarAssistantFreshnessSeconds), options.SolarAssistantFreshnessSeconds),
            (nameof(options.JkBmsFreshnessSeconds), options.JkBmsFreshnessSeconds),
            (nameof(options.Eg4BranchFreshnessSeconds), options.Eg4BranchFreshnessSeconds),
            (nameof(options.MaxDerivationSkewSeconds), options.MaxDerivationSkewSeconds),
        })
            if (value is < 1 or > 3600) failures.Add($"PowerComposition:{field} must be between 1 and 3600.");
        if (options.MaxFutureClockSkewSeconds is < 0 or > 300)
            failures.Add("PowerComposition:MaxFutureClockSkewSeconds must be between 0 and 300.");
        ValidateIds(options.EnabledMpptSourceIds, nameof(options.EnabledMpptSourceIds), failures);
        ValidateIds(options.ExpectedPvTrackerIds, nameof(options.ExpectedPvTrackerIds), failures);
        if ((options.ExpectedPvTrackerIds ?? []).Any(id =>
            id.Count(character => character == '/') != 1 || id.StartsWith('/') || id.EndsWith('/')))
            failures.Add("PowerComposition:ExpectedPvTrackerIds must use SourceId/TrackerId composite IDs.");
        ValidateIds(options.PreferredSmartShuntSourceIds, nameof(options.PreferredSmartShuntSourceIds), failures);
        ValidateIds(options.PreferredSolarAssistantSourceIds, nameof(options.PreferredSolarAssistantSourceIds), failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateIds(List<string>? ids, string field, List<string> failures)
    {
        ids ??= [];
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Any(id => id != id.Trim() || id.Length > 64))
            failures.Add($"PowerComposition:{field} must contain trimmed IDs of 1-64 characters.");
        if (ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Count)
            failures.Add($"PowerComposition:{field} must be unique.");
    }
}
