using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HVO.Edge.Outbox;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Exporter.HomeAssistant;

internal sealed class HomeAssistantOutboxBatchSender(
    IHttpClientFactory clientFactory,
    HomeAssistantExporterCredential credential,
    IOptions<HomeAssistantExporterOptions> options) : IEdgeOutboxBatchSender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<EdgeOutboxSendOutcome>> SendAsync(
        IReadOnlyList<EdgeOutboxRecord> records,
        CancellationToken cancellationToken)
    {
        var parsed = new List<(EdgeOutboxRecord Record, HomeAssistantOutboxPayload Payload)>();
        var outcomes = new List<EdgeOutboxSendOutcome>();
        foreach (var record in records)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<HomeAssistantOutboxPayload>(record.PayloadJson, JsonOptions)
                    ?? throw new JsonException("Payload was null.");
                parsed.Add((record, payload));
            }
            catch (JsonException)
            {
                outcomes.Add(new(record.Id, EdgeOutboxSendStatus.PermanentFailure, "Invalid Home Assistant outbox payload"));
            }
        }

        foreach (var group in parsed.GroupBy(static item => (item.Payload.Contract, item.Record.SourceId)))
        {
            var batch = group.ToArray();
            var endpoint = group.Key.Contract switch
            {
                HomeAssistantExportContract.PowerReading => "api/v1/power/readings",
                HomeAssistantExportContract.WeatherRaw => "api/v1/weather/raw/batch",
                _ => null
            };
            if (endpoint is null)
            {
                outcomes.AddRange(batch.Select(static item => new EdgeOutboxSendOutcome(
                    item.Record.Id, EdgeOutboxSendStatus.PermanentFailure, "Unsupported Home Assistant export contract")));
                continue;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.Value.CentralIngestEndpoint!), endpoint));
                request.Headers.Add("X-Api-Key", credential.CentralApiKey!);
                request.Content = JsonContent.Create(batch.Select(static item => item.Payload.Payload).ToArray(), options: JsonOptions);
                using var response = await clientFactory.CreateClient("HvoEdge").SendAsync(request, cancellationToken);
                var status = Classify(response.StatusCode);
                if (status == EdgeOutboxSendStatus.Sent)
                {
                    var result = await ReadBatchResultAsync(response, cancellationToken);
                    var timestamps = batch.Select(static item => item.Record.RecordedAtUtc).ToHashSet();
                    if (result is null
                        || result.Inserted < 0
                        || result.Skipped < 0
                        || result.Inserted + result.Skipped + result.Failures.Count != batch.Length
                        || result.Failures.Keys.Any(timestamp => !timestamps.Contains(timestamp)))
                    {
                        outcomes.AddRange(batch.Select(static item => new EdgeOutboxSendOutcome(
                            item.Record.Id, EdgeOutboxSendStatus.TransientFailure, "Central ingest returned an invalid batch response")));
                    }
                    else
                    {
                        outcomes.AddRange(batch.Select(item => result.Failures.TryGetValue(item.Record.RecordedAtUtc, out var error)
                            ? new EdgeOutboxSendOutcome(item.Record.Id, EdgeOutboxSendStatus.PermanentFailure, error)
                            : new EdgeOutboxSendOutcome(item.Record.Id, EdgeOutboxSendStatus.Sent)));
                    }
                }
                else
                {
                    outcomes.AddRange(batch.Select(item => new EdgeOutboxSendOutcome(
                        item.Record.Id, status, $"Central ingest returned HTTP {(int)response.StatusCode}")));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                outcomes.AddRange(batch.Select(static item => new EdgeOutboxSendOutcome(
                    item.Record.Id, EdgeOutboxSendStatus.TransientFailure, "Central ingest request failed")));
            }
        }
        return outcomes;
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

    private static async Task<BatchResult?> ReadBatchResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("inserted", out var inserted)
                || !document.RootElement.TryGetProperty("skipped", out var skipped)
                || !document.RootElement.TryGetProperty("failed", out var failures)
                || failures.ValueKind != JsonValueKind.Array)
                return null;
            var result = new Dictionary<DateTime, string>();
            foreach (var failure in failures.EnumerateArray())
            {
                DateTime timestamp;
                if (failure.TryGetProperty("recordedAtUtc", out var utc))
                    timestamp = utc.GetDateTime().ToUniversalTime();
                else if (failure.TryGetProperty("recordedAt", out var recorded))
                    timestamp = recorded.GetDateTime().ToUniversalTime();
                else
                    continue;
                var error = failure.TryGetProperty("error", out var errorElement)
                    ? errorElement.GetString()
                    : null;
                result[timestamp] = string.IsNullOrWhiteSpace(error) ? "Central ingest rejected the record" : error;
            }
            return new BatchResult(inserted.GetInt32(), skipped.GetInt32(), result);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record BatchResult(int Inserted, int Skipped, IReadOnlyDictionary<DateTime, string> Failures);
}
