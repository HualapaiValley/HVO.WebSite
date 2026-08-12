using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;
using HVO.Hardware.VictronSmartShunt.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.Outbox;

internal sealed class SmartShuntOutboxBatchSender(
    IHttpClientFactory clientFactory,
    SmartShuntCentralIngestCredential credential,
    IOptions<SmartShuntOptions> options) : IEdgeOutboxBatchSender
{
    public async Task<IReadOnlyList<EdgeOutboxSendOutcome>> SendAsync(
        IReadOnlyList<EdgeOutboxRecord> records,
        CancellationToken cancellationToken)
    {
        var parsed = new List<(EdgeOutboxRecord Record, SmartShuntObservationPayload Payload)>();
        var outcomes = new Dictionary<long, EdgeOutboxSendOutcome>();
        foreach (var record in records)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<SmartShuntObservationPayload>(record.PayloadJson, JsonSerializerOptions.Web)
                    ?? throw new JsonException("Payload was null.");
                if (!IdentityMatches(record, payload))
                    throw new JsonException("Payload identity does not match its outbox record.");
                parsed.Add((record, payload));
            }
            catch (JsonException)
            {
                outcomes[record.Id] = new(record.Id, EdgeOutboxSendStatus.PermanentFailure, "Invalid SmartShunt observation payload");
            }
        }

        if (parsed.Count > 0)
        {
            var sent = await SendBatchAsync(parsed, cancellationToken);
            foreach (var outcome in sent)
                outcomes[outcome.RecordId] = outcome;
        }
        return records.Select(record => outcomes[record.Id]).ToArray();
    }

    private async Task<IReadOnlyList<EdgeOutboxSendOutcome>> SendBatchAsync(
        IReadOnlyList<(EdgeOutboxRecord Record, SmartShuntObservationPayload Payload)> items,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(new Uri(options.Value.CentralIngestBaseEndpoint!), "api/v1/power/smartshunt-observations/batch"));
            request.Headers.Add("X-Api-Key", credential.ApiKey);
            request.Content = JsonContent.Create(items.Select(static item => item.Payload).ToArray(), options: JsonSerializerOptions.Web);
            using var response = await clientFactory.CreateClient("HvoEdge").SendAsync(request, cancellationToken);
            var status = Classify(response.StatusCode);
            if (status != EdgeOutboxSendStatus.Sent)
                return items.Select(item => new EdgeOutboxSendOutcome(item.Record.Id, status, $"Central ingest returned HTTP {(int)response.StatusCode}")).ToArray();

            var result = await ReadResultAsync(response, cancellationToken);
            var expected = items.Select(static item => (item.Record.SourceId, item.Record.RecordedAtUtc)).ToHashSet();
            if (result is null || result.Inserted < 0 || result.Skipped < 0
                || result.Inserted + result.Skipped + result.Failures.Count != items.Count
                || result.Failures.Keys.Any(key => !expected.Contains(key)))
                return items.Select(static item => new EdgeOutboxSendOutcome(item.Record.Id, EdgeOutboxSendStatus.TransientFailure, "Central ingest returned an invalid batch response")).ToArray();
            return items.Select(item => result.Failures.TryGetValue((item.Record.SourceId, item.Record.RecordedAtUtc), out var error)
                ? new EdgeOutboxSendOutcome(item.Record.Id, EdgeOutboxSendStatus.PermanentFailure, error)
                : new EdgeOutboxSendOutcome(item.Record.Id, EdgeOutboxSendStatus.Sent)).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return items.Select(static item => new EdgeOutboxSendOutcome(item.Record.Id, EdgeOutboxSendStatus.TransientFailure, "Central ingest request failed")).ToArray();
        }
    }

    private static bool IdentityMatches(EdgeOutboxRecord record, SmartShuntObservationPayload payload) =>
        payload.Summary.SourceId == record.SourceId && payload.Summary.DeviceId == record.DeviceId
        && payload.Summary.RecordedAtUtc == record.RecordedAtUtc && payload.Detail.SourceId == record.SourceId
        && payload.Detail.DeviceId == record.DeviceId && payload.Detail.RecordedAtUtc == record.RecordedAtUtc;

    private static async Task<BatchResult?> ReadResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("inserted", out var inserted) || !root.TryGetProperty("skipped", out var skipped)
                || !root.TryGetProperty("failed", out var failed) || failed.ValueKind != JsonValueKind.Array) return null;
            var failures = new Dictionary<(string, DateTime), string>();
            foreach (var item in failed.EnumerateArray())
            {
                if (!item.TryGetProperty("sourceId", out var source) || !item.TryGetProperty("recordedAtUtc", out var recorded)) return null;
                if (!failures.TryAdd((source.GetString() ?? string.Empty, recorded.GetDateTime().ToUniversalTime()),
                    item.TryGetProperty("error", out var error) ? error.GetString() ?? "Rejected" : "Rejected")) return null;
            }
            return new(inserted.GetInt32(), skipped.GetInt32(), failures);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    private static EdgeOutboxSendStatus Classify(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        if (code is >= 200 and < 300) return EdgeOutboxSendStatus.Sent;
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || code >= 500)
            return EdgeOutboxSendStatus.TransientFailure;
        return EdgeOutboxSendStatus.PermanentFailure;
    }

    private sealed record BatchResult(int Inserted, int Skipped, IReadOnlyDictionary<(string SourceId, DateTime RecordedAtUtc), string> Failures);
}
