using System.Text.Json;
using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Bms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace HVO.Hardware.JkBms.Outbox.Forwarders;

/// <summary>
/// Forwards batched BMS readings to the HVO web API over HTTP.
///
/// The POST body is a JSON array of outbox record payloads.
/// Authentication uses the <c>X-Api-Key</c> header.
///
/// Forwarding is skipped (no-op) when <see cref="OutboxOptions.ApiEndpoint"/> is
/// the default placeholder or empty, so the application starts cleanly in development
/// without a running API server.
/// </summary>
public sealed class HttpApiForwarder : IReadingForwarder
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly OutboxOptions _options;
    private readonly ILogger<HttpApiForwarder> _logger;

    private static readonly HashSet<string> PlaceholderEndpoints =
    [
        string.Empty,
        "https://localhost:5001/api/v1/bms/raw",
        "https://localhost:5001/api/v1/bms/readings",
    ];

    // True when the configured endpoint or key looks like a placeholder or is unset,
    // indicating the forwarder should be a no-op until real values are supplied.
    private bool IsPlaceholderConfig =>
        PlaceholderEndpoints.Contains(_options.ApiEndpoint)
        || string.IsNullOrWhiteSpace(_options.ApiKey)
        || string.Equals(_options.ApiKey, "REPLACE_ME", StringComparison.OrdinalIgnoreCase);

    public string Name => "HttpApiForwarder";

    public HttpApiForwarder(
        IHttpClientFactory httpFactory,
        IOptions<OutboxOptions> options,
        ILogger<HttpApiForwarder> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ForwardAsync(IReadOnlyList<EdgeOutboxRecord> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return;

        if (IsPlaceholderConfig)
        {
            _logger.LogDebug(
                "HttpApiForwarder: no real endpoint configured, skipping {Count} record(s).",
                batch.Count);
            return;
        }

        var client = _httpFactory.CreateClient("OutboxForwarder");

        // Deserialise each payload and batch them into a single POST
        var payloads = batch
            .Select(r => JsonSerializer.Deserialize<object>(r.PayloadJson))
            .ToArray();

        _logger.LogDebug(
            "HttpApiForwarder: posting {Count} record(s) to {Endpoint}",
            batch.Count, _options.ApiEndpoint);

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(_options.ApiEndpoint, payloads, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException("Request timed out.");
        }

        if (!response.IsSuccessStatusCode)
        {
            // Read only a bounded prefix to avoid buffering large HTML error pages.
            var body = await ReadBoundedBodyAsync(response, ct);
            var error = $"HTTP {(int)response.StatusCode}: {body}";
            _logger.LogWarning(
                "HttpApiForwarder: HTTP {StatusCode} from {Endpoint}. Response: {Body}",
                (int)response.StatusCode, _options.ApiEndpoint, body);

            if ((int)response.StatusCode is 400 or 401 or 403 or 404)
                throw new PermanentForwarderException(batch.Select(r => (r.Id, error)).ToList());

            throw new HttpRequestException(error);
        }

        var batchResponse = await response.Content.ReadFromJsonAsync<BmsIngestBatchResponse>(cancellationToken: ct);
        var failedRecords = MapFailedRecords(batch, batchResponse?.Failed);
        if (failedRecords.Count > 0)
            throw new PermanentForwarderException(failedRecords);

        _logger.LogInformation(
            "HttpApiForwarder: successfully forwarded {Count} record(s).", batch.Count);
    }

    private static async Task<string> ReadBoundedBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, leaveOpen: true);
        var buffer = new char[512];
        var charsRead = await reader.ReadAsync(buffer, ct);
        return new string(buffer, 0, charsRead);
    }

    private static List<(long RecordId, string Error)> MapFailedRecords(
        IReadOnlyList<EdgeOutboxRecord> batch,
        IReadOnlyList<BmsIngestFailure>? failures)
    {
        if (failures is null || failures.Count == 0)
            return [];

        var result = new List<(long RecordId, string Error)>();
        foreach (var failure in failures)
        {
            var record = batch.FirstOrDefault(r =>
                string.Equals(r.SourceId, failure.DeviceAddress, StringComparison.OrdinalIgnoreCase) &&
                r.RecordedAtUtc == failure.RecordedAtUtc);
            if (record is not null)
                result.Add((record.Id, failure.Error));
        }

        if (result.Count == 0)
            result.AddRange(batch.Select(r => (r.Id, "API response included record failures that could not be matched to the submitted batch.")));

        return result;
    }

    private sealed class BmsIngestBatchResponse
    {
        public IReadOnlyList<BmsIngestFailure> Failed { get; init; } = [];
    }

    private sealed class BmsIngestFailure
    {
        public string DeviceAddress { get; init; } = string.Empty;
        public DateTime RecordedAtUtc { get; init; }
        public string Error { get; init; } = string.Empty;
    }
}
