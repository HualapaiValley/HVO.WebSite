using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HVO.Edge.Contracts.Weather;
using HVO.Edge.Outbox;
using HVO.Hardware.DavisVantagePro2.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

internal sealed class DavisOutboxBatchSender(
    IHttpClientFactory clientFactory,
    DavisCentralIngestCredential credential,
    IOptions<StationOptions> options) : IEdgeOutboxBatchSender
{
    public async Task<IReadOnlyList<EdgeOutboxSendOutcome>> SendAsync(
        IReadOnlyList<EdgeOutboxRecord> records,
        CancellationToken cancellationToken)
    {
        var outcomes = new Dictionary<long, EdgeOutboxSendOutcome>();
        var live = Parse<DavisWeatherLivePayload>(records, DavisOutboxPayloadTypes.Raw, outcomes,
            static (record, payload) => payload.StationId == record.SourceId && payload.RecordedAtUtc == record.RecordedAtUtc);
        var archive = Parse<DavisWeatherArchivePayload>(records, DavisOutboxPayloadTypes.Archive, outcomes,
            static (record, payload) => payload.StationId == record.SourceId && payload.RecordedAtUtc == record.RecordedAtUtc);

        Apply(outcomes, await SendPartitionAsync("api/v1/weather/raw/batch", live, cancellationToken));
        Apply(outcomes, await SendPartitionAsync("api/v1/weather/archive/batch", archive, cancellationToken));
        return records.Select(record => outcomes.TryGetValue(record.Id, out var outcome)
            ? outcome
            : new(record.Id, EdgeOutboxSendStatus.PermanentFailure, "Unsupported Davis payload type")).ToArray();
    }

    private static IReadOnlyList<Parsed<T>> Parse<T>(
        IEnumerable<EdgeOutboxRecord> records,
        string payloadType,
        IDictionary<long, EdgeOutboxSendOutcome> outcomes,
        Func<EdgeOutboxRecord, T, bool> identityMatches)
    {
        var parsed = new List<Parsed<T>>();
        foreach (var record in records.Where(record => record.PayloadType == payloadType))
        {
            try
            {
                var payload = JsonSerializer.Deserialize<T>(record.PayloadJson, JsonSerializerOptions.Web)
                    ?? throw new JsonException("Payload was null.");
                if (!identityMatches(record, payload))
                    throw new JsonException("Payload identity does not match its outbox record.");
                parsed.Add(new(record, payload));
            }
            catch (JsonException)
            {
                outcomes[record.Id] = new(record.Id, EdgeOutboxSendStatus.PermanentFailure, "Invalid Davis outbox payload");
            }
        }
        return parsed;
    }

    private async Task<IReadOnlyList<EdgeOutboxSendOutcome>> SendPartitionAsync<T>(
        string relativeEndpoint,
        IReadOnlyList<Parsed<T>> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return [];
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(new Uri(options.Value.CentralIngestBaseEndpoint!), relativeEndpoint));
            request.Headers.Add("X-Api-Key", credential.ApiKey);
            request.Content = JsonContent.Create(items.Select(static item => item.Payload).ToArray(), options: JsonSerializerOptions.Web);
            using var response = await clientFactory.CreateClient("HvoEdge").SendAsync(request, cancellationToken);
            var status = Classify(response.StatusCode);
            if (status != EdgeOutboxSendStatus.Sent)
                return items.Select(item => new EdgeOutboxSendOutcome(item.Record.Id, status, $"Central ingest returned HTTP {(int)response.StatusCode}")).ToArray();

            var result = await ReadBatchResultAsync(response, cancellationToken);
            var expected = items.Select(static item => (item.Record.SourceId, item.Record.RecordedAtUtc)).ToHashSet();
            if (result is null || result.Inserted < 0 || result.Skipped < 0
                || result.Inserted + result.Skipped + result.Failures.Count != items.Count
                || result.Failures.Keys.Any(key => !expected.Contains(key)))
                return items.Select(static item => new EdgeOutboxSendOutcome(item.Record.Id, EdgeOutboxSendStatus.TransientFailure, "Central ingest returned an invalid batch response")).ToArray();

            return items.Select(item => result.Failures.TryGetValue((item.Record.SourceId, item.Record.RecordedAtUtc), out var error)
                ? new EdgeOutboxSendOutcome(item.Record.Id, EdgeOutboxSendStatus.PermanentFailure, error)
                : new EdgeOutboxSendOutcome(item.Record.Id, EdgeOutboxSendStatus.Sent)).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return items.Select(static item => new EdgeOutboxSendOutcome(item.Record.Id, EdgeOutboxSendStatus.TransientFailure, "Central ingest request failed")).ToArray();
        }
    }

    private static async Task<BatchResult?> ReadBatchResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("inserted", out var inserted) || !root.TryGetProperty("skipped", out var skipped)
                || !root.TryGetProperty("failed", out var failed) || failed.ValueKind != JsonValueKind.Array)
                return null;
            var failures = new Dictionary<(string SourceId, DateTime RecordedAtUtc), string>();
            foreach (var item in failed.EnumerateArray())
            {
                var sourceId = item.TryGetProperty("stationId", out var station) ? station.GetString() : null;
                var timestamp = item.TryGetProperty("recordedAtUtc", out var utc)
                    ? utc.GetDateTime()
                    : item.TryGetProperty("recordedAt", out var recordedAt) ? recordedAt.GetDateTime() : default;
                if (string.IsNullOrWhiteSpace(sourceId) || timestamp == default)
                    return null;
                if (!failures.TryAdd((sourceId, timestamp.ToUniversalTime()), item.TryGetProperty("error", out var error)
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

    private static void Apply(IDictionary<long, EdgeOutboxSendOutcome> current, IEnumerable<EdgeOutboxSendOutcome> updates)
    {
        foreach (var update in updates)
            current[update.RecordId] = update;
    }

    private static EdgeOutboxSendStatus Classify(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        if (code is >= 200 and < 300) return EdgeOutboxSendStatus.Sent;
        if (statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || code >= 500) return EdgeOutboxSendStatus.TransientFailure;
        return EdgeOutboxSendStatus.PermanentFailure;
    }

    private sealed record Parsed<T>(EdgeOutboxRecord Record, T Payload);
    private sealed record BatchResult(int Inserted, int Skipped, IReadOnlyDictionary<(string SourceId, DateTime RecordedAtUtc), string> Failures);
}
