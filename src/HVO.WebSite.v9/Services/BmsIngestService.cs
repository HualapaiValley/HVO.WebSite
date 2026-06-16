using System.ComponentModel.DataAnnotations;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.v9.Services;

public sealed class BmsIngestService : IBmsIngestService
{
    private readonly HvoV9DbContext _db;
    private readonly ILogger<BmsIngestService> _logger;

    public BmsIngestService(HvoV9DbContext db, ILogger<BmsIngestService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<BmsIngestBatchResponse> IngestReadingsAsync(
        IReadOnlyList<BmsIngestRequest> requests,
        CancellationToken ct)
    {
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
                _logger.LogDebug(ex, "Race on device registration — re-fetching");
                var raceAddresses = devicesToAdd.Select(d => d.Address).ToList();
                var raceFetched = await _db.BmsDevices
                    .Where(d => raceAddresses.Contains(d.Address))
                    .ToDictionaryAsync(d => d.Address, StringComparer.OrdinalIgnoreCase, ct);
                foreach (var (addr, dev) in raceFetched)
                    existingDevices[addr] = dev;
            }
        }

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
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await _db.Database.BeginTransactionAsync(ct);

                    if (request.Config is not null)
                        await UpsertDeviceConfigAsync(deviceId, recordedAt, request.Config, ct);

                    if (request.DeviceInfo is not null)
                        await UpsertDeviceInfoAsync(deviceId, recordedAt, request.DeviceInfo, ct);

                    var packVoltageMv = readingReq.PackVoltageMv > 0
                        ? readingReq.PackVoltageMv
                        : readingReq.TotalVoltageMv ?? 0;

                    var powerWatts = packVoltageMv / 1000.0 * readingReq.CurrentMa / 1000.0;
                    var socPercent = readingReq.StateOfChargePercent ?? readingReq.SocPercent;
                    var sohPercent = readingReq.StateOfHealthPercent ?? readingReq.SohPercent;

                    var reading = new BmsReading
                    {
                        DeviceId = deviceId,
                        RecordedAt = recordedAt,
                        PackVoltageMv = packVoltageMv,
                        CurrentMa = readingReq.CurrentMa,
                        PowerWatts = powerWatts,
                        SocPercent = (byte)Math.Clamp(socPercent, 0, 100),
                        SohPercent = (byte)Math.Clamp(sohPercent, 0, 100),
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
                    Error = "The BMS reading could not be ingested.",
                });
                _logger.LogError(ex,
                    "Failed to ingest BMS reading for {Address} at {RecordedAt}",
                    readingReq.DeviceAddress, recordedAt);
            }
        }

        _logger.LogInformation(
            "BMS batch ingest: {Inserted} inserted, {Skipped} skipped, {Failed} failed. Devices: {Addresses}",
            inserted, skipped, failures.Count, string.Join(", ", addresses));

        return new BmsIngestBatchResponse { Inserted = inserted, Skipped = skipped, Failed = failures };
    }

    private async Task UpsertDeviceConfigAsync(int deviceId, DateTime recordedAt, BmsConfigRequest cfg, CancellationToken ct)
    {
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

    private async Task UpsertDeviceInfoAsync(int deviceId, DateTime recordedAt, BmsDeviceInfoRequest info, CancellationToken ct)
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

    private async Task HandleAlarmChangeAsync(int deviceId, DateTime recordedAt, long alarmBitmask, CancellationToken ct)
    {
        var openAlarm = await _db.BmsAlarms
            .Where(a => a.DeviceId == deviceId && a.ClearedAt == null)
            .OrderByDescending(a => a.ActivatedAt)
            .FirstOrDefaultAsync(ct);

        if (alarmBitmask != 0)
        {
            if (openAlarm is null || openAlarm.AlarmBitmask != alarmBitmask)
            {
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
