using System.Text.Json;

namespace HVO.WebSite.v9.Infrastructure;

/// <summary>
/// Handles detection and unwrapping of CloudEvents 1.0 envelope format
/// in batch ingest requests, with backward compatibility for legacy raw payloads.
/// </summary>
public static class CloudEventsBatchUnwrapper
{
    /// <summary>
    /// Detects whether a JSON array uses CloudEvents envelope format by checking
    /// for the <c>specversion</c> field in the first element.
    /// </summary>
    public static bool IsCloudEventsBatch(JsonElement firstElement)
    {
        return firstElement.ValueKind == JsonValueKind.Object
            && firstElement.TryGetProperty("specversion", out var sv)
            && sv.ValueKind == JsonValueKind.String;
    }

    /// <summary>
    /// Unwraps a batch of CloudEvents envelopes, extracting the <c>data</c>
    /// payload from each. Logs a warning and skips records that are malformed
    /// or missing data.
    /// </summary>
    /// <returns>
    /// A tuple of the unwrapped <c>JsonElement</c> data payloads and a list of
    /// error descriptions for records that could not be unwrapped.
    /// </returns>
    public static (List<JsonElement> Data, List<string> Errors) UnwrapBatch(
        JsonElement batch,
        ILogger logger)
    {
        var data = new List<JsonElement>();
        var errors = new List<string>();

        if (batch.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Batch payload is not a JSON array.");
            return (data, errors);
        }

        foreach (var element in batch.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                errors.Add("Batch element is not a JSON object.");
                continue;
            }

            if (!element.TryGetProperty("data", out var dataElement))
            {
                var type = element.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString() : "unknown";
                logger.LogWarning("CloudEvents record missing 'data' field (type: {Type}) — skipped", type);
                errors.Add($"CloudEvents record missing 'data' field.");
                continue;
            }

            var clone = dataElement.Clone();
            data.Add(clone);
        }

        return (data, errors);
    }
}
