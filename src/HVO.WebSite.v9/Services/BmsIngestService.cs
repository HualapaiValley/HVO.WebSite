using System.ComponentModel.DataAnnotations;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.v9.Services;

public sealed class BmsIngestService : IBmsIngestService
{
    private const int HistoryPageSize = 512;
    private readonly HvoV9DbContext _db;
    private readonly ILogger<BmsIngestService> _logger;

    public BmsIngestService(HvoV9DbContext db, ILogger<BmsIngestService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<BmsIngestBatchResponse> IngestReadingsAsync(
        IReadOnlyList<BmsIngestRequest> requests, CancellationToken ct)
    {
        var failures = new List<BmsIngestFailure>();
        var candidates = new List<BmsIngestRequest>();
        foreach (var request in requests)
        {
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(request.Reading, new ValidationContext(request.Reading),
                    errors, validateAllProperties: true))
            {
                failures.Add(new BmsIngestFailure
                {
                    DeviceAddress = request.Reading.DeviceAddress,
                    RecordedAtUtc = request.Reading.RecordedAtUtc.ToUniversalTime(),
                    Error = string.Join("; ", errors.Select(error => error.ErrorMessage))
                });
            }
            else
                candidates.Add(request);
        }
        if (candidates.Count == 0)
            return new BmsIngestBatchResponse { Failed = failures };

        Dictionary<string, int> deviceIds;
        try
        {
            deviceIds = await RegisterDevicesAsync(candidates, ct);
        }
        catch
        {
            _db.ChangeTracker.Clear();
            throw;
        }
        var records = candidates.Select(request => (
            Request: request,
            DeviceId: deviceIds[request.Reading.DeviceAddress.ToUpperInvariant()],
            RecordedAt: request.Reading.RecordedAtUtc.ToUniversalTime())).ToList();

        try
        {
            return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                // A retry must rebuild lookups and entities, including alarm/config state.
                _db.ChangeTracker.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                foreach (var deviceId in records.Select(r => r.DeviceId).Distinct().Order())
                {
                    if (_db.Database.IsSqlServer())
                        await _db.Database.ExecuteSqlInterpolatedAsync(
                            $"SELECT [Id] FROM [v9].[BmsDevice] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {deviceId}", ct);
                }

                var durable = await GetDurableKeysAsync(records, ct);
                var valid = records.Where(r => !durable.Contains((r.DeviceId, r.RecordedAt)))
                    .DistinctBy(r => (r.DeviceId, r.RecordedAt))
                    .OrderBy(r => r.DeviceId).ThenBy(r => r.RecordedAt).ToList();
                var skipped = records.Count - valid.Count;

                // A summary missing requested children is not a successful replay.
                var ids = records.Select(r => r.DeviceId).Distinct().ToArray();
                var times = records.Select(r => r.RecordedAt).Distinct().ToArray();
                var summaries = await _db.BmsReadings.AsNoTracking()
                    .Where(r => ids.Contains(r.DeviceId) && times.Contains(r.RecordedAt))
                    .Select(r => new { r.DeviceId, r.RecordedAt }).ToListAsync(ct);
                if (valid.Any(r => summaries.Any(s => s.DeviceId == r.DeviceId && s.RecordedAt == r.RecordedAt)))
                    throw new IngestPersistenceException("An existing BMS summary is missing required children. Retry cannot be acknowledged.");

                var readings = valid.Select(r => BuildReadingEntity(r.DeviceId, r.RecordedAt, r.Request.Reading)).ToList();
                _db.BmsReadings.AddRange(readings);
                if (readings.Count > 0)
                    await _db.SaveChangesAsync(ct);

                var latestConfigs = await _db.BmsDeviceConfigs.Where(c => ids.Contains(c.DeviceId))
                    .GroupBy(c => c.DeviceId).Select(g => g.OrderByDescending(c => c.RecordedAt).ThenByDescending(c => c.Id).First())
                    .ToDictionaryAsync(c => c.DeviceId, ct);
                var latestInfos = await _db.BmsDeviceInfos.Where(i => ids.Contains(i.DeviceId))
                    .GroupBy(i => i.DeviceId).Select(g => g.OrderByDescending(i => i.RecordedAt).ThenByDescending(i => i.Id).First())
                    .ToDictionaryAsync(i => i.DeviceId, ct);

                for (var index = 0; index < valid.Count; index++)
                {
                    var (request, deviceId, recordedAt) = valid[index];
                    var readingId = readings[index].Id;
                    for (var cell = 0; cell < (request.Reading.CellVoltagesMv?.Count ?? 0); cell++)
                        _db.BmsCellVoltages.Add(new BmsCellVoltage
                        {
                            ReadingId = readingId, CellIndex = checked((byte)(cell + 1)),
                            VoltageMv = request.Reading.CellVoltagesMv![cell]
                        });
                    for (var cell = 0; cell < (request.Reading.CellResistancesMOhm?.Count ?? 0); cell++)
                        _db.BmsCellResistances.Add(new BmsCellResistance
                        {
                            ReadingId = readingId, CellIndex = checked((byte)(cell + 1)),
                            ResistanceMOhm = request.Reading.CellResistancesMOhm![cell]
                        });

                    if (request.Config is not null &&
                        (!latestConfigs.TryGetValue(deviceId, out var lastConfig) || !ConfigEquals(lastConfig, request.Config)))
                    {
                        var config = BuildConfigEntity(deviceId, recordedAt, request.Config);
                        _db.BmsDeviceConfigs.Add(config);
                        if (lastConfig is null || recordedAt >= lastConfig.RecordedAt)
                            latestConfigs[deviceId] = config;
                    }
                    if (request.DeviceInfo is not null &&
                        (!latestInfos.TryGetValue(deviceId, out var lastInfo) || !DeviceInfoEquals(lastInfo, request.DeviceInfo)))
                    {
                        var info = BuildInfoEntity(deviceId, recordedAt, request.DeviceInfo);
                        _db.BmsDeviceInfos.Add(info);
                        if (lastInfo is null || recordedAt >= lastInfo.RecordedAt)
                            latestInfos[deviceId] = info;
                    }
                }

                if (valid.Count > 0)
                {
                    await _db.SaveChangesAsync(ct);
                    foreach (var group in valid.GroupBy(r => r.DeviceId))
                        await RebuildAlarmSuffixAsync(group.Key, group.Min(r => r.RecordedAt), ct);
                }
                var completed = await GetDurableKeysAsync(records, ct);
                if (!records.All(r => completed.Contains((r.DeviceId, r.RecordedAt))))
                    throw new IngestPersistenceException("BMS child identities remain unresolved. Retry is safe.");
                await tx.CommitAsync(ct);
                return new BmsIngestBatchResponse { Inserted = valid.Count, Skipped = skipped, Failed = failures };
            });
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // The explicit transaction is already disposed/rolled back. Clear MARS'
            // failed tracked state before asking which complete identities committed.
            _db.ChangeTracker.Clear();
            var durable = await GetDurableKeysAsync(records, ct);
            if (!records.All(r => durable.Contains((r.DeviceId, r.RecordedAt))))
                throw new IngestPersistenceException("The BMS batch could not be fully accounted. Retry is safe.", ex);
            return new BmsIngestBatchResponse { Skipped = records.Count, Failed = failures };
        }
        catch (Exception exception)
        {
            _db.ChangeTracker.Clear();
            if (exception is not OperationCanceledException)
                _logger.LogError(exception, "BMS batch transaction failed for {Count} records. {Addresses}",
                    records.Count, string.Join(", ", candidates.Select(r => r.Reading.DeviceAddress).Distinct()));
            throw;
        }
    }

    private async Task<Dictionary<string, int>> RegisterDevicesAsync(
        IReadOnlyList<BmsIngestRequest> requests, CancellationToken ct)
    {
        var addresses = requests.Select(r => r.Reading.DeviceAddress.ToUpperInvariant()).Distinct().ToArray();
        return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                _db.ChangeTracker.Clear();
                var existing = await _db.BmsDevices.Where(d => addresses.Contains(d.Address))
                    .ToDictionaryAsync(d => d.Address, StringComparer.OrdinalIgnoreCase, ct);
                var missing = addresses.Where(a => !existing.ContainsKey(a)).ToArray();
                if (missing.Length == 0)
                    return existing.ToDictionary(kv => kv.Key, kv => kv.Value.Id, StringComparer.OrdinalIgnoreCase);
                _db.BmsDevices.AddRange(missing.Select(address => new BmsDevice
                {
                    Address = address,
                    Alias = requests.First(r => r.Reading.DeviceAddress.Equals(address, StringComparison.OrdinalIgnoreCase)).Reading.DeviceAlias,
                    FirstSeenAt = DateTime.UtcNow
                }));
                try
                {
                    await using var registration = await _db.Database.BeginTransactionAsync(ct);
                    await _db.SaveChangesAsync(ct);
                    await registration.CommitAsync(ct);
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    _logger.LogDebug(ex, "BMS device registration conflict; discarding failed tracking before retry");
                }
            }
            _db.ChangeTracker.Clear();
            var final = await _db.BmsDevices.AsNoTracking().Where(d => addresses.Contains(d.Address))
                .ToDictionaryAsync(d => d.Address, d => d.Id, StringComparer.OrdinalIgnoreCase, ct);
            if (final.Count != addresses.Length)
                throw new IngestPersistenceException("BMS device registration remains unresolved. Retry is safe.");
            return final;
        });
    }

    private async Task<HashSet<(int, DateTime)>> GetDurableKeysAsync(
        IReadOnlyList<(BmsIngestRequest Request, int DeviceId, DateTime RecordedAt)> records, CancellationToken ct)
    {
        var ids = records.Select(r => r.DeviceId).Distinct().ToArray();
        var times = records.Select(r => r.RecordedAt).Distinct().ToArray();
        var rows = await _db.BmsReadings.AsNoTracking()
            .Where(r => ids.Contains(r.DeviceId) && times.Contains(r.RecordedAt))
            .Select(r => new
            {
                r.DeviceId, r.RecordedAt,
                VoltageCount = r.CellVoltages.Count,
                VoltageMax = r.CellVoltages.Select(c => (int?)c.CellIndex).Max() ?? 0,
                VoltageMin = r.CellVoltages.Select(c => (int?)c.CellIndex).Min() ?? 0,
                ResistanceCount = r.CellResistances.Count,
                ResistanceMax = r.CellResistances.Select(c => (int?)c.CellIndex).Max() ?? 0,
                ResistanceMin = r.CellResistances.Select(c => (int?)c.CellIndex).Min() ?? 0
            }).ToListAsync(ct);
        return records.GroupBy(record => (record.DeviceId, record.RecordedAt))
            .Where(group => rows.Any(row => row.DeviceId == group.Key.DeviceId && row.RecordedAt == group.Key.RecordedAt
                && group.All(record =>
                    CompleteChildren(record.Request.Reading.CellVoltagesMv?.Count ?? 0, row.VoltageCount, row.VoltageMin, row.VoltageMax)
                    && CompleteChildren(record.Request.Reading.CellResistancesMOhm?.Count ?? 0, row.ResistanceCount, row.ResistanceMin, row.ResistanceMax))))
            .Select(group => group.Key).ToHashSet();
    }

    private static bool CompleteChildren(int required, int count, int minimum, int maximum) =>
        required == 0 || (count >= required && minimum == 1 && maximum == count);

    private async Task RebuildAlarmSuffixAsync(int deviceId, DateTime earliest, CancellationToken ct)
    {
        var predecessor = await _db.BmsReadings.AsNoTracking()
            .Where(r => r.DeviceId == deviceId && r.RecordedAt < earliest)
            .OrderByDescending(r => r.RecordedAt)
            .Select(r => new { r.RecordedAt, r.AlarmBitmask }).FirstOrDefaultAsync(ct);
        BmsAlarm? active = null;
        if (predecessor is { AlarmBitmask: not 0 })
        {
            // Find the actual contiguous predecessor run from authoritative raw
            // readings, rather than trusting a possibly inverted legacy interval.
            var lastDifferent = await _db.BmsReadings.AsNoTracking()
                .Where(r => r.DeviceId == deviceId && r.RecordedAt < earliest && r.AlarmBitmask != predecessor.AlarmBitmask)
                .OrderByDescending(r => r.RecordedAt).Select(r => (DateTime?)r.RecordedAt).FirstOrDefaultAsync(ct);
            var start = await _db.BmsReadings.AsNoTracking()
                .Where(r => r.DeviceId == deviceId && r.RecordedAt < earliest &&
                    (!lastDifferent.HasValue || r.RecordedAt > lastDifferent.Value))
                .MinAsync(r => r.RecordedAt, ct);
            active = await _db.BmsAlarms.Where(a => a.DeviceId == deviceId &&
                    a.AlarmBitmask == predecessor.AlarmBitmask && a.ActivatedAt == start)
                .OrderBy(a => a.Id).FirstOrDefaultAsync(ct)
                ?? new BmsAlarm { DeviceId = deviceId, ActivatedAt = start, AlarmBitmask = predecessor.AlarmBitmask };
        }
        var retainedId = active?.Id ?? 0;
        var rebuildFrom = active?.ActivatedAt ?? earliest;
        await _db.BmsAlarms.Where(a => a.DeviceId == deviceId && a.Id != retainedId &&
                (a.ActivatedAt >= rebuildFrom || a.ClearedAt == null || a.ClearedAt >= earliest))
            .ExecuteDeleteAsync(ct);
        if (active is not null)
        {
            active.ClearedAt = null;
            if (active.Id == 0) _db.BmsAlarms.Add(active);
        }

        DateTime? after = null;
        while (true)
        {
            var page = await _db.BmsReadings.AsNoTracking()
                .Where(r => r.DeviceId == deviceId && r.RecordedAt >= earliest &&
                    (!after.HasValue || r.RecordedAt > after.Value))
                .OrderBy(r => r.RecordedAt).Take(HistoryPageSize)
                .Select(r => new { r.RecordedAt, r.AlarmBitmask }).ToListAsync(ct);
            if (page.Count == 0) break;
            foreach (var row in page)
            {
                if ((active?.AlarmBitmask ?? 0) == row.AlarmBitmask) continue;
                if (active is not null) active.ClearedAt = row.RecordedAt;
                active = row.AlarmBitmask == 0 ? null : new BmsAlarm
                {
                    DeviceId = deviceId, AlarmBitmask = row.AlarmBitmask, ActivatedAt = row.RecordedAt
                };
                if (active is not null) _db.BmsAlarms.Add(active);
            }
            await _db.SaveChangesAsync(ct);
            foreach (var entry in _db.ChangeTracker.Entries<BmsAlarm>().Where(e => e.Entity != active).ToList())
                entry.State = EntityState.Detached;
            after = page[^1].RecordedAt;
        }
    }

    private static BmsReading BuildReadingEntity(int deviceId, DateTime recordedAt, BmsReadingRequest request)
    {
        var voltage = request.PackVoltageMv > 0 ? request.PackVoltageMv : request.TotalVoltageMv ?? 0;
        return new BmsReading
        {
            DeviceId = deviceId, RecordedAt = recordedAt, PackVoltageMv = voltage,
            CurrentMa = request.CurrentMa, PowerWatts = voltage / 1000.0 * request.CurrentMa / 1000.0,
            SocPercent = (byte)Math.Clamp(request.StateOfChargePercent ?? request.SocPercent, 0, 100),
            SohPercent = (byte)Math.Clamp(request.StateOfHealthPercent ?? request.SohPercent, 0, 100),
            RemainingCapacityMah = request.RemainingCapacityMah, NominalCapacityMah = request.NominalCapacityMah,
            CycleCount = request.CycleCount, CycleCapacityMah = request.CycleCapacityMah,
            BatteryTemp1C = request.BatteryTemperature1C, BatteryTemp2C = request.BatteryTemperature2C,
            PowerTubeC = request.PowerTubeTemperatureC, BalancingActive = request.BalancingActive,
            BalancingCurrentMa = request.BalancingCurrentMa, DeltaCellVoltageMv = request.DeltaCellVoltageMv,
            AlarmBitmask = request.AlarmBitmask
        };
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
