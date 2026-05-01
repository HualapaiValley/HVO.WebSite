using Asp.Versioning;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

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
    private readonly HvoV9DbContext _db;
    private readonly ILogger<BmsController> _logger;

    public BmsController(HvoV9DbContext db, ILogger<BmsController> logger)
    {
        _db = db;
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
        [FromBody] IReadOnlyList<BmsIngestRequest> requests,
        CancellationToken ct)
    {
        if (requests.Count == 0)
            return ValidationProblem(detail: "Batch must contain at least one record.");

        // ── Resolve devices (upsert by address) ───────────────────────────────

        // Normalize addresses to uppercase so lookups are case-insensitive regardless of
        // client casing or database collation differences.
        var addresses = requests
            .Select(r => r.Reading.DeviceAddress.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingDevices = await _db.BmsDevices
            .Where(d => addresses.Contains(d.Address))
            .ToDictionaryAsync(d => d.Address, StringComparer.OrdinalIgnoreCase, ct);

        var now = DateTime.UtcNow;
        var devicesToAdd = new List<BmsDevice>();
        foreach (var address in addresses)
        {
            if (!existingDevices.ContainsKey(address))
            {
                var alias = requests
                    .First(r => r.Reading.DeviceAddress.Equals(address, StringComparison.OrdinalIgnoreCase))
                    .Reading.DeviceAlias;
                var device = new BmsDevice
                {
                    Address = address,
                    Alias = alias,
                    FirstSeenAt = now,
                };
                devicesToAdd.Add(device);
                existingDevices[address] = device;
            }
        }

        if (devicesToAdd.Count > 0)
        {
            _db.BmsDevices.AddRange(devicesToAdd);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Race condition — another process registered the same device. Re-fetch.
                _logger.LogDebug(ex, "Race on device registration — re-fetching");
                var raceAddresses = devicesToAdd.Select(d => d.Address).ToList();
                var raceFetched = await _db.BmsDevices
                    .Where(d => raceAddresses.Contains(d.Address))
                    .ToDictionaryAsync(d => d.Address, StringComparer.OrdinalIgnoreCase, ct);
                foreach (var (addr, dev) in raceFetched)
                    existingDevices[addr] = dev;
            }
        }

        // ── Pre-check which (DeviceId, RecordedAt) pairs already exist ────────

        var deviceIdByAddress = existingDevices.ToDictionary(
            kv => kv.Key, kv => kv.Value.Id, StringComparer.OrdinalIgnoreCase);
        var deviceIds = deviceIdByAddress.Values.Distinct().ToList();
        var timestamps = requests.Select(r => r.Reading.RecordedAtUtc.ToUniversalTime()).Distinct().ToList();

        var existingKeys = await _db.BmsReadings
            .Where(r => deviceIds.Contains(r.DeviceId) && timestamps.Contains(r.RecordedAt))
            .Select(r => new { r.DeviceId, r.RecordedAt })
            .ToListAsync(ct);
        var existingSet = existingKeys
            .Select(x => (x.DeviceId, x.RecordedAt))
            .ToHashSet();

        // ── Process each record ───────────────────────────────────────────────

        int inserted = 0;
        int skipped = 0;
        var failures = new List<BmsIngestFailure>();
        var seenInBatch = new HashSet<(int, DateTime)>();

        foreach (var request in requests)
        {
            var readingReq = request.Reading;
            var recordedAt = readingReq.RecordedAtUtc.ToUniversalTime();
            var deviceId = deviceIdByAddress[readingReq.DeviceAddress.ToUpperInvariant()];

            if (existingSet.Contains((deviceId, recordedAt)))
            {
                skipped++;
                continue;
            }

            if (!seenInBatch.Add((deviceId, recordedAt)))
            {
                skipped++;
                continue;
            }

            // Validate individual record (data annotations)
            var validationResults = new List<ValidationResult>();
            if (!Validator.TryValidateObject(
                readingReq,
                new ValidationContext(readingReq),
                validationResults,
                validateAllProperties: true))
            {
                failures.Add(new BmsIngestFailure
                {
                    DeviceAddress = readingReq.DeviceAddress,
                    RecordedAtUtc = recordedAt,
                    Error = string.Join("; ", validationResults.Select(r => r.ErrorMessage))
                });
                continue;
            }

            try
            {
                // SqlServerRetryingExecutionStrategy requires that manual transactions
                // are executed inside CreateExecutionStrategy().ExecuteAsync() so that
                // the whole unit (begin → commit) can be retried on transient failures.
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await _db.Database.BeginTransactionAsync(ct);

                    // ── Config snapshot (change-detect on server) ─────────────────

                    if (request.Config is not null)
                        await UpsertDeviceConfigAsync(deviceId, recordedAt, request.Config, ct);

                    // ── Device info snapshot (change-detect on server) ────────────

                    if (request.DeviceInfo is not null)
                        await UpsertDeviceInfoAsync(deviceId, recordedAt, request.DeviceInfo, ct);

                    // ── Reading row ───────────────────────────────────────────────

                    var packVoltageMv = readingReq.PackVoltageMv > 0
                        ? readingReq.PackVoltageMv
                        : readingReq.TotalVoltageMv ?? 0;

                    var powerWatts = packVoltageMv / 1000.0 * readingReq.CurrentMa / 1000.0;

                    var reading = new BmsReading
                    {
                        DeviceId = deviceId,
                        RecordedAt = recordedAt,
                        PackVoltageMv = packVoltageMv,
                        CurrentMa = readingReq.CurrentMa,
                        PowerWatts = powerWatts,
                        SocPercent = (byte)Math.Clamp(readingReq.SocPercent, 0, 100),
                        SohPercent = (byte)Math.Clamp(readingReq.SohPercent, 0, 100),
                        RemainingCapacityMah = readingReq.RemainingCapacityMah,
                        NominalCapacityMah = readingReq.NominalCapacityMah,
                        CycleCount = readingReq.CycleCount,
                        CycleCapacityMah = readingReq.CycleCapacityMah,
                        BatteryTemp1C = readingReq.BatteryTemperature1C,
                        BatteryTemp2C = readingReq.BatteryTemperature2C,
                        PowerTubeC = readingReq.PowerTubeTemperatureC,
                        BalancingActive = readingReq.BalancingActive,
                        BalancingCurrentMa = readingReq.BalancingCurrentMa,
                        DeltaCellVoltageMv = readingReq.DeltaCellVoltageMv,
                        AlarmBitmask = readingReq.AlarmBitmask,
                    };

                    _db.BmsReadings.Add(reading);
                    await _db.SaveChangesAsync(ct);

                    // ── Per-cell voltage rows ─────────────────────────────────────

                    if (readingReq.CellVoltagesMv is { Count: > 0 })
                    {
                        var voltageRows = readingReq.CellVoltagesMv
                            .Select((v, i) => new BmsCellVoltage
                            {
                                ReadingId = reading.Id,
                                CellIndex = (byte)(i + 1),
                                VoltageMv = v,
                            })
                            .ToList();
                        _db.BmsCellVoltages.AddRange(voltageRows);
                    }

                    // ── Per-cell resistance rows ──────────────────────────────────

                    if (readingReq.CellResistancesMOhm is { Count: > 0 })
                    {
                        var resistanceRows = readingReq.CellResistancesMOhm
                            .Select((r, i) => new BmsCellResistance
                            {
                                ReadingId = reading.Id,
                                CellIndex = (byte)(i + 1),
                                ResistanceMOhm = r,
                            })
                            .ToList();
                        _db.BmsCellResistances.AddRange(resistanceRows);
                    }

                    if (readingReq.CellVoltagesMv is { Count: > 0 } || readingReq.CellResistancesMOhm is { Count: > 0 })
                        await _db.SaveChangesAsync(ct);

                    // ── Alarm change-detection ────────────────────────────────────

                    await HandleAlarmChangeAsync(deviceId, recordedAt, readingReq.AlarmBitmask, ct);

                    await tx.CommitAsync(ct);

                    inserted++;

                    _logger.LogDebug(
                        "Ingested BMS reading {Id} for device {Address} at {RecordedAt}",
                        reading.Id, readingReq.DeviceAddress, recordedAt);
                });
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                skipped++;
                _logger.LogDebug(ex,
                    "Duplicate BMS reading for {Address} at {RecordedAt} — skipped",
                    readingReq.DeviceAddress, recordedAt);
            }
            catch (Exception ex)
            {
                failures.Add(new BmsIngestFailure
                {
                    DeviceAddress = readingReq.DeviceAddress,
                    RecordedAtUtc = recordedAt,
                    Error = ex.Message,
                });
                _logger.LogError(ex,
                    "Failed to ingest BMS reading for {Address} at {RecordedAt}",
                    readingReq.DeviceAddress, recordedAt);
            }
        }

        _logger.LogInformation(
            "BMS batch ingest: {Inserted} inserted, {Skipped} skipped, {Failed} failed. Devices: {Addresses}",
            inserted, skipped, failures.Count, string.Join(", ", addresses));

        return CreatedAtAction(nameof(IngestReadings), new { },
            new BmsIngestBatchResponse { Inserted = inserted, Skipped = skipped, Failed = failures });
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task UpsertDeviceConfigAsync(
        int deviceId, DateTime recordedAt, BmsConfigRequest cfg, CancellationToken ct)
    {
        // Only insert a new snapshot when it differs from the last one stored.
        var last = await _db.BmsDeviceConfigs
            .Where(c => c.DeviceId == deviceId)
            .OrderByDescending(c => c.RecordedAt)
            .FirstOrDefaultAsync(ct);

        if (last is not null && ConfigEquals(last, cfg))
            return;

        _db.BmsDeviceConfigs.Add(new BmsDeviceConfig
        {
            DeviceId = deviceId,
            RecordedAt = recordedAt,
            CellCount = cfg.CellCount,
            NominalCapacityMah = cfg.NominalCapacityMah,
            ChargingEnabled = cfg.ChargingEnabled,
            DischargingEnabled = cfg.DischargingEnabled,
            BalancingEnabled = cfg.BalancingEnabled,
            CellOvpMv = cfg.CellOvpMv,
            CellOvpRecoveryMv = cfg.CellOvpRecoveryMv,
            CellUvpMv = cfg.CellUvpMv,
            CellUvpRecoveryMv = cfg.CellUvpRecoveryMv,
            BalanceTriggerMv = cfg.BalanceTriggerMv,
            BalanceStartVoltageMv = cfg.BalanceStartVoltageMv,
            ChargeOcpMa = cfg.ChargeOcpMa,
            ChargeOcpDelayS = cfg.ChargeOcpDelayS,
            ChargeOcpRecoveryS = cfg.ChargeOcpRecoveryS,
            DischargeOcpMa = cfg.DischargeOcpMa,
            DischargeOcpDelayS = cfg.DischargeOcpDelayS,
            DischargeOcpRecoveryS = cfg.DischargeOcpRecoveryS,
            ShortCircuitDelayUs = cfg.ShortCircuitDelayUs,
            ShortCircuitRecoveryS = cfg.ShortCircuitRecoveryS,
            ChargeOtpC = cfg.ChargeOtpC,
            ChargeOtpRecoveryC = cfg.ChargeOtpRecoveryC,
            ChargeUtpC = cfg.ChargeUtpC,
            ChargeUtpRecoveryC = cfg.ChargeUtpRecoveryC,
            DischargeOtpC = cfg.DischargeOtpC,
            DischargeOtpRecoveryC = cfg.DischargeOtpRecoveryC,
            MosOtpC = cfg.MosOtpC,
            MosOtpRecoveryC = cfg.MosOtpRecoveryC,
        });

        await _db.SaveChangesAsync(ct);
    }

    private async Task UpsertDeviceInfoAsync(
        int deviceId, DateTime recordedAt, BmsDeviceInfoRequest info, CancellationToken ct)
    {
        var last = await _db.BmsDeviceInfos
            .Where(i => i.DeviceId == deviceId)
            .OrderByDescending(i => i.RecordedAt)
            .FirstOrDefaultAsync(ct);

        if (last is not null && DeviceInfoEquals(last, info))
            return;

        _db.BmsDeviceInfos.Add(new BmsDeviceInfo
        {
            DeviceId = deviceId,
            RecordedAt = recordedAt,
            Manufacturer = info.Manufacturer,
            Hardware = info.Hardware,
            Firmware = info.Firmware,
            SerialNumber = info.SerialNumber,
            DeviceName = info.DeviceName,
            ManufacturingDate = info.ManufacturingDate,
            UserData = info.UserData,
        });

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Detects alarm transitions for a single reading and maintains open/closed alarm rows.
    ///
    /// Each distinct non-zero alarm bitmask that appears for the first time (or after being
    /// cleared) opens a new <see cref="BmsAlarm"/>. When the bitmask returns to zero the
    /// most-recent open alarm for that device is closed.
    /// </summary>
    private async Task HandleAlarmChangeAsync(
        int deviceId, DateTime recordedAt, long alarmBitmask, CancellationToken ct)
    {
        var openAlarm = await _db.BmsAlarms
            .Where(a => a.DeviceId == deviceId && a.ClearedAt == null)
            .OrderByDescending(a => a.ActivatedAt)
            .FirstOrDefaultAsync(ct);

        if (alarmBitmask != 0)
        {
            // New alarm state — open a row if there isn't already an open one with this mask.
            if (openAlarm is null || openAlarm.AlarmBitmask != alarmBitmask)
            {
                // Close any open alarm with a different mask first.
                if (openAlarm is not null)
                {
                    openAlarm.ClearedAt = recordedAt;
                    _db.BmsAlarms.Update(openAlarm);
                }

                _db.BmsAlarms.Add(new BmsAlarm
                {
                    DeviceId = deviceId,
                    AlarmBitmask = alarmBitmask,
                    ActivatedAt = recordedAt,
                });

                await _db.SaveChangesAsync(ct);
            }
        }
        else if (openAlarm is not null)
        {
            // Alarms cleared — close the open alarm.
            openAlarm.ClearedAt = recordedAt;
            _db.BmsAlarms.Update(openAlarm);
            await _db.SaveChangesAsync(ct);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx &&
        (sqlEx.Number == 2601 || sqlEx.Number == 2627);

    private static bool ConfigEquals(BmsDeviceConfig stored, BmsConfigRequest request) =>
        stored.CellCount == request.CellCount &&
        stored.NominalCapacityMah == request.NominalCapacityMah &&
        stored.ChargingEnabled == request.ChargingEnabled &&
        stored.DischargingEnabled == request.DischargingEnabled &&
        stored.BalancingEnabled == request.BalancingEnabled &&
        stored.CellOvpMv == request.CellOvpMv &&
        stored.CellOvpRecoveryMv == request.CellOvpRecoveryMv &&
        stored.CellUvpMv == request.CellUvpMv &&
        stored.CellUvpRecoveryMv == request.CellUvpRecoveryMv &&
        stored.BalanceTriggerMv == request.BalanceTriggerMv &&
        stored.BalanceStartVoltageMv == request.BalanceStartVoltageMv &&
        stored.ChargeOcpMa == request.ChargeOcpMa &&
        stored.ChargeOcpDelayS == request.ChargeOcpDelayS &&
        stored.ChargeOcpRecoveryS == request.ChargeOcpRecoveryS &&
        stored.DischargeOcpMa == request.DischargeOcpMa &&
        stored.DischargeOcpDelayS == request.DischargeOcpDelayS &&
        stored.DischargeOcpRecoveryS == request.DischargeOcpRecoveryS &&
        stored.ShortCircuitDelayUs == request.ShortCircuitDelayUs &&
        stored.ShortCircuitRecoveryS == request.ShortCircuitRecoveryS &&
        stored.ChargeOtpC == request.ChargeOtpC &&
        stored.ChargeOtpRecoveryC == request.ChargeOtpRecoveryC &&
        stored.ChargeUtpC == request.ChargeUtpC &&
        stored.ChargeUtpRecoveryC == request.ChargeUtpRecoveryC &&
        stored.DischargeOtpC == request.DischargeOtpC &&
        stored.DischargeOtpRecoveryC == request.DischargeOtpRecoveryC &&
        stored.MosOtpC == request.MosOtpC &&
        stored.MosOtpRecoveryC == request.MosOtpRecoveryC;

    private static bool DeviceInfoEquals(BmsDeviceInfo stored, BmsDeviceInfoRequest request) =>
        stored.Manufacturer == request.Manufacturer &&
        stored.Hardware == request.Hardware &&
        stored.Firmware == request.Firmware &&
        stored.SerialNumber == request.SerialNumber &&
        stored.DeviceName == request.DeviceName &&
        stored.ManufacturingDate == request.ManufacturingDate &&
        stored.UserData == request.UserData;
}
