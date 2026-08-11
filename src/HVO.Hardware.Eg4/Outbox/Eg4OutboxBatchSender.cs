using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HVO.Edge.Outbox;
using HVO.Hardware.Eg4.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Outbox;

internal sealed class Eg4OutboxBatchSender(
    IHttpClientFactory clientFactory,
    Eg4CentralIngestCredential credential,
    IOptions<Eg4Options> options) : IEdgeOutboxBatchSender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<EdgeOutboxSendOutcome>> SendAsync(
        IReadOnlyList<EdgeOutboxRecord> records,
        CancellationToken cancellationToken)
    {
        var parsed = new List<(EdgeOutboxRecord Record, Eg4ObservationBundle Bundle)>();
        var outcomes = new Dictionary<long, EdgeOutboxSendOutcome>();
        foreach (var record in records)
        {
            try
            {
                var bundle = JsonSerializer.Deserialize<Eg4ObservationBundle>(record.PayloadJson, JsonOptions)
                    ?? throw new JsonException("Payload was null.");
                if (bundle.Reading is null
                    || !string.Equals(bundle.Reading.SourceId, record.SourceId, StringComparison.Ordinal)
                    || !string.Equals(bundle.Reading.DeviceId, record.DeviceId, StringComparison.Ordinal)
                    || bundle.Reading.RecordedAtUtc != record.RecordedAtUtc)
                    throw new JsonException("Bundle identity does not match its outbox record.");
                parsed.Add((record, bundle));
            }
            catch (JsonException)
            {
                outcomes[record.Id] = new(record.Id, EdgeOutboxSendStatus.PermanentFailure, "Invalid EG4 observation bundle");
            }
        }

        var readingItems = parsed
            .Select(static item => new BatchItem<object>(item.Record, item.Bundle.Reading))
            .ToArray();
        Apply(outcomes, await SendBatchAsync("api/v1/power/readings", readingItems, cancellationToken));

        var sent = parsed.Where(item => outcomes[item.Record.Id].Status == EdgeOutboxSendStatus.Sent).ToArray();
        var mpptItems = sent
            .Where(static item => item.Bundle.MpptDetail is not null)
            .Select(static item => new BatchItem<object>(item.Record, item.Bundle.MpptDetail!))
            .ToArray();
        ApplyFailures(outcomes, await SendBatchAsync("api/v1/power/mppt-detail/batch", mpptItems, cancellationToken));

        sent = sent.Where(item => outcomes[item.Record.Id].Status == EdgeOutboxSendStatus.Sent).ToArray();
        var inverterItems = sent
            .Where(static item => item.Bundle.InverterDetail is not null)
            .Select(static item => new BatchItem<object>(item.Record, item.Bundle.InverterDetail!))
            .ToArray();
        ApplyFailures(outcomes, await SendBatchAsync("api/v1/power/inverter-detail/batch", inverterItems, cancellationToken));

        return records.Select(record => outcomes[record.Id]).ToArray();
    }

    private async Task<IReadOnlyList<EdgeOutboxSendOutcome>> SendBatchAsync<T>(
        string endpoint,
        IReadOnlyList<BatchItem<T>> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return [];
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.Value.CentralIngestEndpoint!), endpoint));
            request.Headers.Add("X-Api-Key", credential.ApiKey);
            request.Content = JsonContent.Create(items.Select(static item => item.Payload).ToArray(), options: JsonOptions);
            using var response = await clientFactory.CreateClient("HvoEdge").SendAsync(request, cancellationToken);
            var status = Classify(response.StatusCode);
            if (status != EdgeOutboxSendStatus.Sent)
            {
                return items.Select(item => new EdgeOutboxSendOutcome(
                    item.Record.Id, status, $"Central ingest returned HTTP {(int)response.StatusCode}")).ToArray();
            }

            var result = await ReadBatchResultAsync(response, cancellationToken);
            var expected = items.Select(static item => (item.Record.SourceId, item.Record.RecordedAtUtc)).ToHashSet();
            if (result is null
                || result.Inserted < 0
                || result.Skipped < 0
                || result.Inserted + result.Skipped + result.Failures.Count != items.Count
                || result.Failures.Keys.Any(key => !expected.Contains(key)))
            {
                return items.Select(static item => new EdgeOutboxSendOutcome(
                    item.Record.Id, EdgeOutboxSendStatus.TransientFailure, "Central ingest returned an invalid batch response")).ToArray();
            }

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
            return items.Select(static item => new EdgeOutboxSendOutcome(
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
            var failures = new Dictionary<(string SourceId, DateTime RecordedAtUtc), string>();
            foreach (var item in failed.EnumerateArray())
            {
                if (!item.TryGetProperty("sourceId", out var sourceId)
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
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void Apply(
        IDictionary<long, EdgeOutboxSendOutcome> current,
        IEnumerable<EdgeOutboxSendOutcome> updates)
    {
        foreach (var update in updates)
            current[update.RecordId] = update;
    }

    private static void ApplyFailures(
        IDictionary<long, EdgeOutboxSendOutcome> current,
        IEnumerable<EdgeOutboxSendOutcome> updates)
    {
        foreach (var update in updates)
        {
            if (update.Status != EdgeOutboxSendStatus.Sent)
                current[update.RecordId] = update;
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

    private sealed record BatchItem<T>(EdgeOutboxRecord Record, T Payload);
    private sealed record BatchResult(
        int Inserted,
        int Skipped,
        IReadOnlyDictionary<(string SourceId, DateTime RecordedAtUtc), string> Failures);
}
