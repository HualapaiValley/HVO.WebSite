using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Outbox;

public sealed class EdgeOutboxOptions
{
    public const string SectionName = "Outbox";

    [Required]
    public string DatabasePath { get; set; } = "/app/data/outbox.db";

    [Required]
    public string PayloadType { get; set; } = "edge.telemetry";

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

        if (string.IsNullOrWhiteSpace(options.PayloadType))
            return ValidateOptionsResult.Fail("Outbox:PayloadType is required.");

        if (string.IsNullOrWhiteSpace(options.PayloadVersion))
            return ValidateOptionsResult.Fail("Outbox:PayloadVersion is required.");

        return ValidateOptionsResult.Success;
    }
}
