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
        var outcomes = new List<EdgeOutboxSendOutcome>(records.Count);
        foreach (var record in records)
        {
            Eg4ObservationBundle bundle;
            try
            {
                bundle = JsonSerializer.Deserialize<Eg4ObservationBundle>(record.PayloadJson, JsonOptions)
                    ?? throw new JsonException("Payload was null.");
                if (bundle.Reading is null
                    || !string.Equals(bundle.Reading.SourceId, record.SourceId, StringComparison.Ordinal)
                    || !string.Equals(bundle.Reading.DeviceId, record.DeviceId, StringComparison.Ordinal)
                    || bundle.Reading.RecordedAtUtc != record.RecordedAtUtc)
                    throw new JsonException("Bundle identity does not match its outbox record.");
            }
            catch (JsonException)
            {
                outcomes.Add(new(record.Id, EdgeOutboxSendStatus.PermanentFailure, "Invalid EG4 observation bundle"));
                continue;
            }

            try
            {
                var outcome = await SendReadingAsync(record, bundle, cancellationToken);
                if (outcome.Status == EdgeOutboxSendStatus.Sent && bundle.MpptDetail is not null)
                    outcome = await SendDetailAsync(record.Id, "api/v1/power/mppt-detail", bundle.MpptDetail, cancellationToken);
                if (outcome.Status == EdgeOutboxSendStatus.Sent && bundle.InverterDetail is not null)
                    outcome = await SendDetailAsync(record.Id, "api/v1/power/inverter-detail", bundle.InverterDetail, cancellationToken);
                outcomes.Add(outcome);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                outcomes.Add(new(record.Id, EdgeOutboxSendStatus.TransientFailure, "Central ingest request failed"));
            }
            catch (TaskCanceledException)
            {
                outcomes.Add(new(record.Id, EdgeOutboxSendStatus.TransientFailure, "Central ingest request timed out"));
            }
        }
        return outcomes;
    }

    private async Task<EdgeOutboxSendOutcome> SendReadingAsync(
        EdgeOutboxRecord record,
        Eg4ObservationBundle bundle,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync("api/v1/power/readings", new[] { bundle.Reading }, cancellationToken);
        var status = Classify(response.StatusCode);
        if (status != EdgeOutboxSendStatus.Sent)
            return new(record.Id, status, $"Central ingest returned HTTP {(int)response.StatusCode}");

        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("inserted", out var inserted)
                || !root.TryGetProperty("skipped", out var skipped)
                || !root.TryGetProperty("failed", out var failed)
                || failed.ValueKind != JsonValueKind.Array
                || inserted.GetInt32() < 0
                || skipped.GetInt32() < 0
                || inserted.GetInt32() + skipped.GetInt32() + failed.GetArrayLength() != 1)
                return new(record.Id, EdgeOutboxSendStatus.TransientFailure, "Central ingest returned an invalid batch response");
            if (failed.GetArrayLength() > 0)
                return new(record.Id, EdgeOutboxSendStatus.PermanentFailure, "Central ingest rejected the EG4 reading");
            return new(record.Id, EdgeOutboxSendStatus.Sent);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return new(record.Id, EdgeOutboxSendStatus.TransientFailure, "Central ingest returned an invalid batch response");
        }
    }

    private async Task<EdgeOutboxSendOutcome> SendDetailAsync<T>(
        long recordId,
        string endpoint,
        T payload,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(endpoint, payload, cancellationToken);
        var status = Classify(response.StatusCode);
        return status == EdgeOutboxSendStatus.Sent
            ? new(recordId, EdgeOutboxSendStatus.Sent)
            : new(recordId, status, $"Central ingest returned HTTP {(int)response.StatusCode}");
    }

    private async Task<HttpResponseMessage> SendAsync<T>(string endpoint, T payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.Value.CentralIngestEndpoint!), endpoint));
        request.Headers.Add("X-Api-Key", credential.ApiKey);
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        return await clientFactory.CreateClient("HvoEdge").SendAsync(request, cancellationToken);
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
}
