using Asp.Versioning;
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

    public BmsController(IBmsIngestService ingestService)
    {
        _ingestService = ingestService;
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
        [FromBody] IReadOnlyList<BmsIngestRequest> requests,
        CancellationToken ct)
    {
        if (requests.Count == 0)
            return ValidationProblem(detail: "Batch must contain at least one record.");

        if (requests.Count > MaxBatchSize)
            return ValidationProblem(detail: $"Batch size {requests.Count} exceeds the maximum of {MaxBatchSize} records. Split the batch or reduce the outbox batch size on the gateway.");

        var requestValidationErrors = ValidateRequests(requests);
        if (requestValidationErrors.Count > 0)
        {
            foreach (var (key, messages) in requestValidationErrors)
                foreach (var message in messages)
                    ModelState.AddModelError(key, message);

            return ValidationProblem(ModelState);
        }

        var response = await _ingestService.IngestReadingsAsync(requests, ct);

        return CreatedAtAction(nameof(IngestReadings), new { },
            response);
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

}
