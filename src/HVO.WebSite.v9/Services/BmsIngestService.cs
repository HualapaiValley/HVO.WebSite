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

        var skipped = 0;
        var failures = new List<BmsIngestFailure>();
        var seenInBatch = new HashSet<(int, DateTime)>();

        var validRecords = new List<(BmsIngestRequest Request, int DeviceId, DateTime RecordedAt)>();

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

            validRecords.Add((request, deviceId, recordedAt));
        }

        if (validRecords.Count == 0)
        {
            _logger.LogInformation(
                "BMS batch ingest: 0 inserted, {Skipped} skipped, {Failed} failed. Devices: {Addresses}",
                skipped, failures.Count, string.Join(", ", addresses));

            return new BmsIngestBatchResponse { Inserted = 0, Skipped = skipped, Failed = failures };
        }

        var distinctDeviceIds = validRecords.Select(v => v.DeviceId).Distinct().ToList();

        var latestConfigs = await _db.BmsDeviceConfigs
            .Where(c => distinctDeviceIds.Contains(c.DeviceId))
            .GroupBy(c => c.DeviceId)
            .Select(g => g.OrderByDescending(c => c.RecordedAt).First())
            .ToDictionaryAsync(c => c.DeviceId, ct);

        var latestInfos = await _db.BmsDeviceInfos
            .Where(i => distinctDeviceIds.Contains(i.DeviceId))
            .GroupBy(i => i.DeviceId)
            .Select(g => g.OrderByDescending(i => i.RecordedAt).First())
            .ToDictionaryAsync(i => i.DeviceId, ct);

        var openAlarms = await _db.BmsAlarms
            .Where(a => distinctDeviceIds.Contains(a.DeviceId) && a.ClearedAt == null)
            .ToDictionaryAsync(a => a.DeviceId, ct);

        var inserted = 0;

        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);

                var readings = new List<BmsReading>(validRecords.Count);
                var readingIndexByKey = new Dictionary<(int, DateTime), int>();

                foreach (var (request, deviceId, recordedAt) in validRecords)
                {
                    var readingReq = request.Reading;

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

                    readings.Add(reading);
                    readingIndexByKey[(deviceId, recordedAt)] = readings.Count - 1;
                }

                _db.BmsReadings.AddRange(readings);
                await _db.SaveChangesAsync(ct);

                var cellVoltages = new List<BmsCellVoltage>();
                var cellResistances = new List<BmsCellResistance>();

                foreach (var (request, deviceId, recordedAt) in validRecords)
                {
                    var readingReq = request.Reading;
                    var readingIndex = readingIndexByKey[(deviceId, recordedAt)];
                    var readingId = readings[readingIndex].Id;

                    if (readingReq.CellVoltagesMv is { Count: > 0 })
                    {
                        for (var i = 0; i < readingReq.CellVoltagesMv.Count; i++)
                        {
                            cellVoltages.Add(new BmsCellVoltage
                            {
                                ReadingId = readingId,
                                CellIndex = (byte)(i + 1),
                                VoltageMv = readingReq.CellVoltagesMv[i],
                            });
                        }
                    }

                    if (readingReq.CellResistancesMOhm is { Count: > 0 })
                    {
                        for (var i = 0; i < readingReq.CellResistancesMOhm.Count; i++)
                        {
                            cellResistances.Add(new BmsCellResistance
                            {
                                ReadingId = readingId,
                                CellIndex = (byte)(i + 1),
                                ResistanceMOhm = readingReq.CellResistancesMOhm[i],
                            });
                        }
                    }
                }

                if (cellVoltages.Count > 0)
                    _db.BmsCellVoltages.AddRange(cellVoltages);
                if (cellResistances.Count > 0)
                    _db.BmsCellResistances.AddRange(cellResistances);

                foreach (var (request, deviceId, recordedAt) in validRecords)
                {
                    if (request.Config is not null)
                    {
                        if (!latestConfigs.TryGetValue(deviceId, out var lastConfig) ||
                            !ConfigEquals(lastConfig, request.Config))
                        {
                            _db.BmsDeviceConfigs.Add(BuildConfigEntity(deviceId, recordedAt, request.Config));
                            latestConfigs[deviceId] = BuildConfigEntity(deviceId, recordedAt, request.Config);
                        }
                    }

                    if (request.DeviceInfo is not null)
                    {
                        if (!latestInfos.TryGetValue(deviceId, out var lastInfo) ||
                            !DeviceInfoEquals(lastInfo, request.DeviceInfo))
                        {
                            _db.BmsDeviceInfos.Add(BuildInfoEntity(deviceId, recordedAt, request.DeviceInfo));
                            latestInfos[deviceId] = BuildInfoEntity(deviceId, recordedAt, request.DeviceInfo);
                        }
                    }
                }

                foreach (var (request, deviceId, recordedAt) in validRecords)
                {
                    var alarmBitmask = request.Reading.AlarmBitmask;
                    var hasOpenAlarm = openAlarms.TryGetValue(deviceId, out var openAlarm);

                    if (alarmBitmask != 0)
                    {
                        if (!hasOpenAlarm || openAlarm!.AlarmBitmask != alarmBitmask)
                        {
                            if (hasOpenAlarm)
                            {
                                openAlarm!.ClearedAt = recordedAt;
                                _db.BmsAlarms.Update(openAlarm);
                            }

                            var newAlarm = new BmsAlarm
                            {
                                DeviceId = deviceId,
                                AlarmBitmask = alarmBitmask,
                                ActivatedAt = recordedAt,
                            };
                            _db.BmsAlarms.Add(newAlarm);
                            openAlarms[deviceId] = newAlarm;
                        }
                    }
                    else if (hasOpenAlarm)
                    {
                        openAlarm!.ClearedAt = recordedAt;
                        _db.BmsAlarms.Update(openAlarm);
                        openAlarms.Remove(deviceId);
                    }
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                inserted = validRecords.Count;

                _logger.LogInformation(
                    "BMS batch ingest: {Inserted} inserted, {Skipped} skipped, {Failed} failed in single TX. Devices: {Addresses}",
                    inserted, skipped, failures.Count, string.Join(", ", addresses));

                foreach (var (request, deviceId, recordedAt) in validRecords)
                {
                    _logger.LogDebug(
                        "Ingested BMS reading for device {Address} at {RecordedAt}",
                        request.Reading.DeviceAddress, recordedAt);
                }
            });
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex,
                "BMS batch insert hit constraint violation; {Count} records skipped. {Addresses}",
                validRecords.Count, string.Join(", ", addresses));
            skipped += validRecords.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "BMS batch transaction failed for {Count} records. Marking all as failures. {Addresses}",
                validRecords.Count, string.Join(", ", addresses));

            foreach (var (request, _, recordedAt) in validRecords)
            {
                failures.Add(new BmsIngestFailure
                {
                    DeviceAddress = request.Reading.DeviceAddress,
                    RecordedAtUtc = recordedAt,
                    Error = "Batch ingest failed; record could not be ingested.",
                });
            }
        }

        if (inserted > 0)
        {
            _logger.LogInformation(
                "BMS batch ingest: {Inserted} inserted, {Skipped} skipped, {Failed} failed. Devices: {Addresses}",
                inserted, skipped, failures.Count, string.Join(", ", addresses));
        }

        return new BmsIngestBatchResponse { Inserted = inserted, Skipped = skipped, Failed = failures };
    }

    private static BmsDeviceConfig BuildConfigEntity(int deviceId, DateTime recordedAt, BmsConfigRequest cfg) =>
        new()
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
        };

    private static BmsDeviceInfo BuildInfoEntity(int deviceId, DateTime recordedAt, BmsDeviceInfoRequest info) =>
        new()
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
        };

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
