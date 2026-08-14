using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Outbox;

internal sealed class JkBmsOutboxBatchSender(
    IHttpClientFactory clientFactory,
    JkBmsCentralIngestCredential credential,
    IOptions<JkBmsOptions> options) : IEdgeOutboxBatchSender
{
    public async Task<IReadOnlyList<EdgeOutboxSendOutcome>> SendAsync(
        IReadOnlyList<EdgeOutboxRecord> records,
        CancellationToken cancellationToken)
    {
        var ready = new List<(EdgeOutboxRecord Record, JsonElement Data)>();
        var outcomes = new Dictionary<long, EdgeOutboxSendOutcome>();
        foreach (var record in records)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<BmsIngressRecord>(record.PayloadJson, JsonSerializerOptions.Web)
                    ?? throw new JsonException("Payload was null.");
                if (payload.Reading is null
                    || !string.Equals(payload.Reading.DeviceAddress, record.SourceId, StringComparison.OrdinalIgnoreCase)
                    || payload.Reading.RecordedAtUtc != record.RecordedAtUtc)
                    throw new JsonException("Payload identity does not match its outbox record.");
                ready.Add((record, JsonSerializer.SerializeToElement(payload, JsonSerializerOptions.Web)));
            }
            catch (JsonException)
            {
                outcomes[record.Id] = new(record.Id, EdgeOutboxSendStatus.PermanentFailure, "Invalid JK BMS observation payload");
            }
        }

        if (ready.Count > 0)
        {
            var sent = await SendBatchAsync(ready, cancellationToken);
            foreach (var outcome in sent)
                outcomes[outcome.RecordId] = outcome;
        }
        return records.Select(record => outcomes.TryGetValue(record.Id, out var outcome)
            ? outcome
            : new EdgeOutboxSendOutcome(record.Id, EdgeOutboxSendStatus.TransientFailure, "No JK BMS send outcome was produced"))
            .ToArray();
    }

    private async Task<IReadOnlyList<EdgeOutboxSendOutcome>> SendBatchAsync(
        IReadOnlyList<(EdgeOutboxRecord Record, JsonElement Data)> ready,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.CentralIngestEndpoint);
            request.Headers.Add("X-Api-Key", credential.ApiKey);
            request.Content = JsonContent.Create(
                CloudEventsForwardingHelper.WrapBatchAsCloudEvents(ready, "jkbms"),
                options: JsonSerializerOptions.Web);
            using var response = await clientFactory.CreateClient("HvoEdge").SendAsync(request, cancellationToken);
            var status = Classify(response.StatusCode);
            if (status != EdgeOutboxSendStatus.Sent)
            {
                return ready.Select(item => new EdgeOutboxSendOutcome(
                    item.Record.Id, status, $"Central ingest returned HTTP {(int)response.StatusCode}")).ToArray();
            }

            var result = await ReadBatchResultAsync(response, cancellationToken);
            var expected = ready
                .Select(static item => (SourceId: item.Record.SourceId.ToUpperInvariant(), item.Record.RecordedAtUtc))
                .ToHashSet();
            if (result is null
                || result.Inserted < 0
                || result.Skipped < 0
                || result.Inserted + result.Skipped + result.Failures.Count != ready.Count
                || result.Failures.Keys.Any(key => !expected.Contains((key.SourceId.ToUpperInvariant(), key.RecordedAtUtc))))
            {
                return ready.Select(static item => new EdgeOutboxSendOutcome(
                    item.Record.Id, EdgeOutboxSendStatus.TransientFailure, "Central ingest returned an invalid batch response")).ToArray();
            }

            return ready.Select(item => result.Failures.TryGetValue(
                    (item.Record.SourceId, item.Record.RecordedAtUtc),
                    out var error)
                ? new EdgeOutboxSendOutcome(
                    item.Record.Id,
                    EdgeOutboxSendStatus.PermanentFailure,
                    $"Central ingest rejected the record: {NormalizeRejectionReason(error)}")
                : new EdgeOutboxSendOutcome(item.Record.Id, EdgeOutboxSendStatus.Sent))
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ready.Select(static item => new EdgeOutboxSendOutcome(
                item.Record.Id, EdgeOutboxSendStatus.TransientFailure, "Central ingest request failed")).ToArray();
        }
    }

    private static async Task<BatchResult?> ReadBatchResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("inserted", out var inserted)
                || !root.TryGetProperty("skipped", out var skipped)
                || !root.TryGetProperty("failed", out var failed)
                || failed.ValueKind != JsonValueKind.Array)
                return null;

            var failures = new Dictionary<(string SourceId, DateTime RecordedAtUtc), string>(new FailureKeyComparer());
            foreach (var item in failed.EnumerateArray())
            {
                if (!item.TryGetProperty("deviceAddress", out var sourceId)
                    || !item.TryGetProperty("recordedAtUtc", out var recordedAtUtc))
                    return null;
                var key = (sourceId.GetString() ?? string.Empty, recordedAtUtc.GetDateTime().ToUniversalTime());
                if (!failures.TryAdd(key, item.TryGetProperty("error", out var error)
                    ? error.GetString() ?? "Central ingest rejected the record"
                    : "Central ingest rejected the record"))
                    return null;
            }
            return new(inserted.GetInt32(), skipped.GetInt32(), failures);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static EdgeOutboxSendStatus Classify(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        if (code is >= 200 and < 300)
            return EdgeOutboxSendStatus.Sent;
        if (statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || code >= 500)
            return EdgeOutboxSendStatus.TransientFailure;
        return EdgeOutboxSendStatus.PermanentFailure;
    }

    private static string NormalizeRejectionReason(string? error) =>
        string.IsNullOrWhiteSpace(error) ? "No rejection reason was provided" : error.Trim();

    private sealed record BatchResult(
        int Inserted,
        int Skipped,
        IReadOnlyDictionary<(string SourceId, DateTime RecordedAtUtc), string> Failures);

    private sealed class FailureKeyComparer : IEqualityComparer<(string SourceId, DateTime RecordedAtUtc)>
    {
        public bool Equals((string SourceId, DateTime RecordedAtUtc) x, (string SourceId, DateTime RecordedAtUtc) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.SourceId, y.SourceId) && x.RecordedAtUtc == y.RecordedAtUtc;

        public int GetHashCode((string SourceId, DateTime RecordedAtUtc) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SourceId), obj.RecordedAtUtc);
    }
}
