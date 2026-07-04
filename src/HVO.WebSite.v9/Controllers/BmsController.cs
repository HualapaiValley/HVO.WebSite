using System.Text.Json;
using Asp.Versioning;
using HVO.WebSite.v9.Infrastructure;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HVO.WebSite.v9.Controllers;

/// <summary>
/// BMS API — ingest battery monitor readings from JK BMS hardware devices.
/// All ingest endpoints require the <c>ingest:bms</c> scope claim.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/bms")]
[Tags("BMS")]
public class BmsController : ControllerBase
{
    /// <summary>
    /// Maximum number of records per ingest batch. Values above this threshold
    /// risk exceeding SQL Server's 2100-parameter limit in IN-list deduplication
    /// queries and are rejected with HTTP 400.
    /// </summary>
    public const int MaxBatchSize = 500;

    private readonly IBmsIngestService _ingestService;
    private readonly ILogger<BmsController> _logger;

    public BmsController(IBmsIngestService ingestService, ILogger<BmsController> logger)
    {
        _ingestService = ingestService;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // INGEST
    // -------------------------------------------------------------------------

    /// <summary>
    /// Ingest a batch of BMS readings from hardware devices.
    /// </summary>
    /// <remarks>
    /// Idempotent: duplicate records (same DeviceAddress + RecordedAtUtc) are silently skipped.
    ///
    /// Each record may optionally include a <c>Config</c> and/or <c>DeviceInfo</c> snapshot.
    /// These are upserted only when the payload is non-null (hardware sends them only on change).
    ///
    /// Alarm change-detection runs for each record: bits that transition 0→1 open a new
    /// <see cref="BmsAlarm"/> row; bits that return to 0 close the most-recent open alarm.
    ///
    /// Requires an API key with the <c>ingest:bms</c> scope claim.
    /// </remarks>
    /// <response code="201">Batch processed. See Inserted/Skipped/Failed counts in response body.</response>
    /// <response code="400">Empty batch or malformed request body.</response>
    /// <response code="401">Missing or invalid API key.</response>
    /// <response code="403">API key does not have the ingest:bms scope.</response>
    [HttpPost("readings")]
    [Authorize(Policy = "BmsIngest")]
    [ProducesResponseType(typeof(BmsIngestBatchResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<BmsIngestBatchResponse>> IngestReadings(
        [FromBody] JsonElement batch,
        CancellationToken ct)
    {
        // Detect and unwrap CloudEvents 1.0 envelope format, or use raw payloads for backward compat
        List<JsonElement> rawPayloads;
        if (batch.ValueKind == JsonValueKind.Array)
        {
            var first = batch.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object && CloudEventsBatchUnwrapper.IsCloudEventsBatch(first))
            {
                var (data, errors) = CloudEventsBatchUnwrapper.UnwrapBatch(batch, _logger);
                if (errors.Count > 0 && data.Count == 0)
                    return ValidationProblem(detail: $"Failed to unwrap CloudEvents batch: {string.Join("; ", errors.Take(3))}");
                rawPayloads = data;
            }
            else
            {
                rawPayloads = batch.EnumerateArray().Select(e => e.Clone()).ToList();
            }
        }
        else
        {
            return ValidationProblem(detail: "Request body must be a JSON array.");
        }

        if (rawPayloads.Count == 0)
            return ValidationProblem(detail: "Batch must contain at least one valid record.");

        if (rawPayloads.Count > MaxBatchSize)
            return ValidationProblem(detail: $"Batch size {rawPayloads.Count} exceeds the maximum of {MaxBatchSize} records. Split the batch or reduce the outbox batch size on the gateway.");

        // Deserialize each (possibly unwrapped) payload
        var deserFailures = new List<BmsIngestFailure>();
        var requests = new List<BmsIngestRequest>();
        foreach (var (payload, index) in rawPayloads.Select((p, i) => (p, i)))
        {
            try
            {
                var request = payload.Deserialize<BmsIngestRequest>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (request is not null)
                    requests.Add(request);
                else
                {
                    deserFailures.Add(ExtractBmsFailureMetadata(payload, $"Record at index {index} deserialized to null."));
                    _logger.LogWarning("BMS record at index {Index} deserialized to null — reported as failure", index);
                }
            }
            catch (JsonException ex)
            {
                deserFailures.Add(ExtractBmsFailureMetadata(payload, $"Record at index {index}: invalid JSON — {ex.Message}"));
                _logger.LogWarning(ex, "BMS record at index {Index} has invalid JSON — reported as failure", index);
            }
        }

        if (requests.Count == 0 && deserFailures.Count > 0)
        {
            return CreatedAtAction(nameof(IngestReadings), new { },
                new BmsIngestBatchResponse { Inserted = 0, Skipped = 0, Failed = deserFailures });
        }

        if (requests.Count == 0)
        {
            return CreatedAtAction(nameof(IngestReadings), new { },
                new BmsIngestBatchResponse { Inserted = 0, Skipped = 0, Failed = [] });
        }

        var requestValidationErrors = ValidateRequests(requests);
        if (requestValidationErrors.Count > 0)
        {
            foreach (var (key, messages) in requestValidationErrors)
                foreach (var message in messages)
                    ModelState.AddModelError(key, message);

            return ValidationProblem(ModelState);
        }

        var response = await _ingestService.IngestReadingsAsync(requests, ct);

        if (deserFailures.Count > 0)
        {
            response = new BmsIngestBatchResponse
            {
                Inserted = response.Inserted,
                Skipped = response.Skipped,
                Failed = [.. response.Failed, .. deserFailures],
            };
        }

        return CreatedAtAction(nameof(IngestReadings), new { }, response);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static Dictionary<string, string[]> ValidateRequests(IReadOnlyList<BmsIngestRequest> requests)
    {
        var errors = new Dictionary<string, string[]>();

        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            if (request.Reading is null)
            {
                errors[$"[{index}].reading"] = ["Reading is required."];
                continue;
            }

            var reading = request.Reading;
            if (string.IsNullOrWhiteSpace(reading.DeviceAddress))
                errors[$"[{index}].reading.deviceAddress"] = ["DeviceAddress is required."];
            if (string.IsNullOrWhiteSpace(reading.DeviceAlias))
                errors[$"[{index}].reading.deviceAlias"] = ["DeviceAlias is required."];
        }

        return errors;
    }

    private static BmsIngestFailure ExtractBmsFailureMetadata(
        JsonElement payload, string error)
    {
        string? deviceAddress = null;
        DateTime? recordedAtUtc = null;
        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("reading", out var reading) && reading.ValueKind == JsonValueKind.Object)
            {
                if (reading.TryGetProperty("deviceAddress", out var da) && da.ValueKind == JsonValueKind.String)
                    deviceAddress = da.GetString();
            }

            if (payload.TryGetProperty("recordedAtUtc", out var ra) && ra.ValueKind == JsonValueKind.String)
                recordedAtUtc = ra.GetDateTime();
            else if (payload.TryGetProperty("recordedAt", out var rad) && rad.ValueKind == JsonValueKind.String)
                recordedAtUtc = rad.GetDateTime();
        }

        return new BmsIngestFailure
        {
            DeviceAddress = deviceAddress ?? string.Empty,
            RecordedAtUtc = recordedAtUtc ?? DateTime.UtcNow,
            Error = error,
        };
    }
}
