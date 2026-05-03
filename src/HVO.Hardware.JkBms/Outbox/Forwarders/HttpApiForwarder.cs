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

    public async Task ForwardAsync(IReadOnlyList<OutboxRecord> batch, CancellationToken ct)
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
            .Select(r => System.Text.Json.JsonSerializer.Deserialize<object>(r.Payload))
            .ToArray();

        _logger.LogDebug(
            "HttpApiForwarder: posting {Count} record(s) to {Endpoint}",
            batch.Count, _options.ApiEndpoint);

        var response = await client.PostAsJsonAsync(_options.ApiEndpoint, payloads, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Read only a bounded prefix to avoid buffering large HTML error pages.
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, leaveOpen: true);
            var buffer = new char[512];
            var charsRead = await reader.ReadAsync(buffer, ct);
            var body = new string(buffer, 0, charsRead);
            _logger.LogWarning(
                "HttpApiForwarder: HTTP {StatusCode} from {Endpoint}. Response: {Body}",
                (int)response.StatusCode, _options.ApiEndpoint, body);
        }
        response.EnsureSuccessStatusCode();

        _logger.LogInformation(
            "HttpApiForwarder: successfully forwarded {Count} record(s).", batch.Count);
    }
}
