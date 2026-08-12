using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Outbox;

public sealed class EdgeOutboxOptions
{
    public const int MaxPayloadTypes = 8;
    public const string SectionName = "Outbox";

    [Required]
    public string DatabasePath { get; set; } = "/app/data/outbox.db";

    [Required]
    public string PayloadType { get; set; } = "edge.telemetry";

    public List<string> PayloadTypes { get; set; } = [];

    [Required]
    public string PayloadVersion { get; set; } = "1";

    [Range(1, 500)]
    public int BatchSize { get; set; } = 50;

    [Range(1, 60)]
    public int SweepIntervalSeconds { get; set; } = 5;

    [Range(1, 100)]
    public int MaxRetryAttempts { get; set; } = 10;

    [Range(10, 3600)]
    public int MaxBackoffSeconds { get; set; } = 300;

    [Range(0, 3650)]
    public int SentRetentionDays { get; set; } = 7;

    [Range(0, 3650)]
    public int FailedRetentionDays { get; set; } = 30;

    public IReadOnlyList<string> EffectivePayloadTypes => PayloadTypes.Count == 0
        ? [PayloadType]
        : PayloadTypes;
}

internal sealed class EdgeOutboxOptionsValidator : IValidateOptions<EdgeOutboxOptions>
{
    public ValidateOptionsResult Validate(string? name, EdgeOutboxOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DatabasePath))
            return ValidateOptionsResult.Fail("Outbox:DatabasePath is required.");

        if (!Path.IsPathFullyQualified(options.DatabasePath))
            return ValidateOptionsResult.Fail("Outbox:DatabasePath must be an absolute path.");

        if (string.IsNullOrWhiteSpace(Path.GetFileName(options.DatabasePath)))
            return ValidateOptionsResult.Fail("Outbox:DatabasePath must identify a database file.");

        if (options.PayloadTypes.Count == 0 && string.IsNullOrWhiteSpace(options.PayloadType))
            return ValidateOptionsResult.Fail("Outbox:PayloadType is required.");

        if (options.PayloadTypes.Count > EdgeOutboxOptions.MaxPayloadTypes)
            return ValidateOptionsResult.Fail($"Outbox:PayloadTypes supports at most {EdgeOutboxOptions.MaxPayloadTypes} entries.");

        if (options.PayloadTypes.Any(string.IsNullOrWhiteSpace))
            return ValidateOptionsResult.Fail("Outbox:PayloadTypes cannot contain empty entries.");

        if (options.PayloadTypes.Any(static value => value != value.Trim()))
            return ValidateOptionsResult.Fail("Outbox:PayloadTypes entries must be trimmed.");

        if (options.PayloadTypes.Select(static value => value.Trim()).Distinct(StringComparer.Ordinal).Count() != options.PayloadTypes.Count)
            return ValidateOptionsResult.Fail("Outbox:PayloadTypes entries must be unique.");

        if (string.IsNullOrWhiteSpace(options.PayloadVersion))
            return ValidateOptionsResult.Fail("Outbox:PayloadVersion is required.");

        return ValidateOptionsResult.Success;
    }
}
