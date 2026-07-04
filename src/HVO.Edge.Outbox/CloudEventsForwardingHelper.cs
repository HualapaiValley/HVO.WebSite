using System.Text.Json;
using HVO.Edge.Contracts;

namespace HVO.Edge.Outbox;

/// <summary>
/// Helper for converting outbox records into CloudEvents-wrapped JSON payloads
/// ready for HTTP delivery. Used by all gateway forwarders.
/// </summary>
public static class CloudEventsForwardingHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Wraps a batch of pre-parsed outbox records in CloudEvents envelopes
    /// and serializes them as a JSON array ready for HTTP POST.
    /// </summary>
    /// <param name="ready">Records with their pre-parsed JSON data.</param>
    /// <param name="gatewayType">Gateway identifier for the CloudEvents source URI (e.g. "davis", "jkbms").</param>
    /// <returns>A list of <c>JsonElement</c> CloudEvents envelopes.</returns>
    public static List<JsonElement> WrapBatchAsCloudEvents(
        IReadOnlyList<(EdgeOutboxRecord Record, JsonElement Data)> ready,
        string gatewayType)
    {
        var envelopes = new List<JsonElement>(ready.Count);

        foreach (var (record, data) in ready)
        {
            var cloudEventType = EdgePayloadTypes.ToCloudEventType(record.PayloadType);
            var source = $"/gateways/{gatewayType}/{record.SourceId}";

            var envelope = new Dictionary<string, object?>
            {
                ["specversion"] = CloudEventsConstants.SpecVersion,
                ["type"] = cloudEventType,
                ["source"] = source,
                ["id"] = Guid.NewGuid().ToString("D"),
                ["time"] = record.RecordedAtUtc.ToString("O"),
                ["datacontenttype"] = CloudEventsConstants.JsonContentType,
                ["data"] = data,
            };

            // Add optional extension attributes
            if (!string.IsNullOrEmpty(record.DeviceId))
                envelope["hvo_deviceid"] = record.DeviceId;

            if (!string.IsNullOrEmpty(record.PayloadVersion))
                envelope["hvo_payloadversion"] = record.PayloadVersion;

            var json = JsonSerializer.SerializeToElement(envelope, JsonOptions);
            envelopes.Add(json);
        }

        return envelopes;
    }
}
