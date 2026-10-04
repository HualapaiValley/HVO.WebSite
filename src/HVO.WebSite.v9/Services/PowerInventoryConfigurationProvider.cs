using System.Text.Json;
using HVO.DataModels.Data;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Configuration;
using HVO.WebSite.v9.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.v9.Services;

public sealed class PowerInventoryConfigurationProvider(
    HvoV9DbContext db,
    IOptions<PowerCompositionOptions> compositionOptions,
    TimeProvider timeProvider,
    ILogger<PowerInventoryConfigurationProvider>? logger = null) : IPowerInventoryConfigurationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    // One normal detail read per bucket; malformed data may try two older candidates.
    private const int HistoryCandidatesPerBucket = 3;
    private readonly HvoV9DbContext _db = db;

    public PowerInventoryConfigurationProvider(HvoV9DbContext db)
        : this(db, Options.Create(new PowerCompositionOptions()), TimeProvider.System, null) { }

    public async Task<(PowerDeviceInventorySnapshotResponse Inventory, PowerConfigurationSnapshotResponse Configuration)> GetLatestAsync(
        string sourceId = "solarassistant-total",
        int staleAfterMinutes = 1440,
        CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(sourceId) ? "solarassistant-total" : sourceId.Trim();
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var inventory = await GetLatestInventoryRowAsync(normalized, FutureCutoffUtc(nowUtc), ct);
        var configuration = await GetLatestConfigurationRowAsync(normalized, FutureCutoffUtc(nowUtc), ct);
        return (MapInventory(normalized, inventory, staleAfterMinutes, nowUtc), MapConfiguration(normalized, configuration, staleAfterMinutes, nowUtc));
    }

    public async Task<PowerInverterDetailSnapshotResponse> GetLatestInverterDetailAsync(
        string sourceId,
        int staleAfterMinutes = 5,
        CancellationToken ct = default)
    {
        var normalized = NormalizeSourceId(sourceId);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var futureCutoffUtc = FutureCutoffUtc(nowUtc);
        var rows = await _db.PowerInverterDetailSnapshots
            .AsNoTracking()
            .Where(item => item.SourceId == normalized && item.RecordedAt > DateTime.MinValue && item.RecordedAt <= futureCutoffUtc)
            .OrderByDescending(item => item.RecordedAt)
            .ThenByDescending(item => item.Id)
            .Take(100)
            .ToArrayAsync(ct);
        return rows.Select(row => TryMapInverterDetail(normalized, row, staleAfterMinutes, nowUtc))
            .OfType<PowerInverterDetailSnapshotResponse>()
            .FirstOrDefault()
            ?? new PowerInverterDetailSnapshotResponse { SourceId = normalized, IsPresent = false, IsStale = true };
    }

    public async Task<PowerMpptDetailSnapshotResponse> GetLatestMpptDetailAsync(
        string sourceId,
        int staleAfterMinutes = 5,
        CancellationToken ct = default)
    {
        var normalized = NormalizeSourceId(sourceId);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var futureCutoffUtc = FutureCutoffUtc(nowUtc);
        var rows = await _db.PowerMpptDetailSnapshots
            .AsNoTracking()
            .Where(item => item.SourceId == normalized && item.RecordedAt > DateTime.MinValue && item.RecordedAt <= futureCutoffUtc)
            .OrderByDescending(item => item.RecordedAt)
            .ThenByDescending(item => item.Id)
            .Take(100)
            .ToArrayAsync(ct);
        return rows.Select(row => TryMapMpptDetail(normalized, row, staleAfterMinutes, nowUtc))
            .OfType<PowerMpptDetailSnapshotResponse>()
            .FirstOrDefault()
            ?? new PowerMpptDetailSnapshotResponse { SourceId = normalized, IsPresent = false, IsStale = true };
    }

    public Task<PowerTelemetryHistoryResponse> GetRecentTelemetryAsync(
        IReadOnlyCollection<string> mpptSourceIds,
        IReadOnlyCollection<string> batterySourceIds,
        DateTime sinceUtc,
        CancellationToken ct = default)
        => GetTelemetryWindowAsync(mpptSourceIds, batterySourceIds, sinceUtc, timeProvider.GetUtcNow().UtcDateTime, ct);

    public async Task<PowerTelemetryHistoryResponse> GetTelemetryWindowAsync(
        IReadOnlyCollection<string> mpptSourceIds,
        IReadOnlyCollection<string> batterySourceIds,
        DateTime sinceUtc,
        DateTime untilUtc,
        CancellationToken ct = default)
    {
        var normalizedMppt = mpptSourceIds.Select(NormalizeSourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var normalizedBattery = batterySourceIds.Select(NormalizeSourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Stored datetime2 and the caller's Unspecified boundary both represent UTC.
        sinceUtc = DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc);
        var nowUtc = DateTime.SpecifyKind(untilUtc, DateTimeKind.Utc);
        var mpptDetails = new List<PowerMpptDetailSnapshotResponse>();
        foreach (var sourceId in normalizedMppt)
        {
            // Select IDs before loading JSON. Date parts translate on SQL Server and
            // SQLite; grouping UTC storage values does not depend on the host zone.
            var buckets = (await _db.PowerMpptDetailSnapshots
                .AsNoTracking()
                .Where(row => row.SourceId == sourceId && row.RecordedAt >= sinceUtc && row.RecordedAt <= nowUtc)
                .GroupBy(row => new { row.RecordedAt.Year, row.RecordedAt.Month, row.RecordedAt.Day, row.RecordedAt.Hour, Minute = row.RecordedAt.Minute / 5 })
                .Select(group => group.OrderByDescending(row => row.RecordedAt).ThenByDescending(row => row.Id)
                    .Take(HistoryCandidatesPerBucket).Select(row => row.Id).ToArray())
                .ToArrayAsync(ct)).ToList();
            for (var candidate = 0; candidate < HistoryCandidatesPerBucket && buckets.Count > 0; candidate++)
            {
                var ids = buckets.Where(bucket => bucket.Length > candidate).Select(bucket => bucket[candidate]).ToArray();
                if (ids.Length == 0) break;
                var rows = await _db.PowerMpptDetailSnapshots.AsNoTracking().Where(row => ids.Contains(row.Id)).ToArrayAsync(ct);
                var acceptedIds = new HashSet<long>();
                foreach (var row in rows)
                {
                    var detail = TryMapMpptDetail(row.SourceId, row, int.MaxValue, nowUtc);
                    if (detail is null) continue;
                    mpptDetails.Add(detail);
                    acceptedIds.Add(row.Id);
                }
                buckets.RemoveAll(bucket => bucket.Length > candidate && acceptedIds.Contains(bucket[candidate]));
            }
            if (buckets.Count > 0)
                logger?.LogWarning("Power history has {BucketCount} buckets without a valid bounded representative for source {SourceId}; chart gaps remain explicit", buckets.Count, sourceId);
        }
        var batteryRows = new List<PowerBatteryHistoryPoint>();
        foreach (var sourceId in normalizedBattery)
        {
            var ids = await _db.PowerReadings
                .AsNoTracking()
                .Where(row => row.SourceId == sourceId && row.RecordedAt >= sinceUtc && row.RecordedAt <= nowUtc)
                .GroupBy(row => new { row.DeviceId, row.RecordedAt.Year, row.RecordedAt.Month, row.RecordedAt.Day, row.RecordedAt.Hour, Minute = row.RecordedAt.Minute / 5 })
                .Select(group => group.OrderByDescending(row => row.RecordedAt).ThenByDescending(row => row.Id).Select(row => row.Id).First())
                .ToArrayAsync(ct);
            if (ids.Length == 0) continue;
            var selected = await _db.PowerReadings.AsNoTracking().Where(row => ids.Contains(row.Id))
                .Select(row => new PowerBatteryHistoryPoint(row.RecordedAt, row.SourceId, row.DeviceId, row.BatteryPowerW))
                .ToArrayAsync(ct);
            batteryRows.AddRange(selected.Select(row => row with { RecordedAtUtc = DateTime.SpecifyKind(row.RecordedAtUtc, DateTimeKind.Utc) }));
        }

        return new PowerTelemetryHistoryResponse(
            mpptDetails.OrderBy(row => row.RecordedAtUtc).ThenBy(row => row.SourceId, StringComparer.Ordinal).ThenBy(row => row.Id).ToArray(),
            batteryRows.OrderBy(row => row.RecordedAtUtc).ThenBy(row => row.SourceId, StringComparer.Ordinal).ThenBy(row => row.DeviceId, StringComparer.Ordinal).ToArray());
    }

    private Task<DataModels.Models.V9.PowerDeviceInventorySnapshot?> GetLatestInventoryRowAsync(string sourceId, DateTime futureCutoffUtc, CancellationToken ct) =>
        _db.PowerDeviceInventorySnapshots
            .AsNoTracking()
            .Where(r => r.SourceId == sourceId && r.RecordedAt > DateTime.MinValue && r.RecordedAt <= futureCutoffUtc)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);

    private Task<DataModels.Models.V9.PowerConfigurationSnapshot?> GetLatestConfigurationRowAsync(string sourceId, DateTime futureCutoffUtc, CancellationToken ct) =>
        _db.PowerConfigurationSnapshots
            .AsNoTracking()
            .Where(r => r.SourceId == sourceId && r.RecordedAt > DateTime.MinValue && r.RecordedAt <= futureCutoffUtc)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);

    private PowerDeviceInventorySnapshotResponse MapInventory(string sourceId, DataModels.Models.V9.PowerDeviceInventorySnapshot? row, int staleAfterMinutes, DateTime nowUtc)
    {
        if (row is null)
            return new PowerDeviceInventorySnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true };

        var payload = JsonSerializer.Deserialize<PowerDeviceInventoryPayload>(row.PayloadJson, JsonOptions);
        return new PowerDeviceInventorySnapshotResponse
        {
            SourceId = row.SourceId,
            SourceSystem = row.SourceSystem,
            DeviceId = row.DeviceId,
            RecordedAtUtc = DateTime.SpecifyKind(row.RecordedAt, DateTimeKind.Utc),
            IsPresent = true,
            IsStale = nowUtc - DateTime.SpecifyKind(row.RecordedAt, DateTimeKind.Utc) > TimeSpan.FromMinutes(staleAfterMinutes),
            RestMetricCount = row.RestMetricCount,
            MqttEntityCount = row.MqttEntityCount,
            MqttStateTopicCount = row.MqttStateTopicCount,
            Devices = payload?.Devices ?? [],
        };
    }

    private PowerConfigurationSnapshotResponse MapConfiguration(string sourceId, DataModels.Models.V9.PowerConfigurationSnapshot? row, int staleAfterMinutes, DateTime nowUtc)
    {
        if (row is null)
            return new PowerConfigurationSnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true };

        var payload = JsonSerializer.Deserialize<PowerConfigurationPayload>(row.PayloadJson, JsonOptions);
        return new PowerConfigurationSnapshotResponse
        {
            SourceId = row.SourceId,
            SourceSystem = row.SourceSystem,
            DeviceId = row.DeviceId,
            RecordedAtUtc = DateTime.SpecifyKind(row.RecordedAt, DateTimeKind.Utc),
            IsPresent = true,
            IsStale = nowUtc - DateTime.SpecifyKind(row.RecordedAt, DateTimeKind.Utc) > TimeSpan.FromMinutes(staleAfterMinutes),
            Settings = payload?.Settings ?? [],
            CommandCapabilities = payload?.CommandCapabilities ?? [],
        };
    }

    private PowerInverterDetailSnapshotResponse MapInverterDetail(
        string sourceId,
        DataModels.Models.V9.PowerInverterDetailSnapshot? row,
        int staleAfterMinutes,
        PowerInverterDetailPayload? payload,
        DateTime nowUtc)
    {
        if (row is null)
            return new PowerInverterDetailSnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true };

        return new PowerInverterDetailSnapshotResponse
        {
            SourceId = row.SourceId,
            SourceSystem = row.SourceSystem,
            DeviceId = row.DeviceId,
            RecordedAtUtc = DateTime.SpecifyKind(row.RecordedAt, DateTimeKind.Utc),
            IsPresent = true,
            IsStale = nowUtc - DateTime.SpecifyKind(row.RecordedAt, DateTimeKind.Utc) > TimeSpan.FromMinutes(staleAfterMinutes),
            PvStrings = payload?.PvStrings ?? [],
            Ac = payload?.Ac,
            Load = payload?.Load,
            Battery = payload?.Battery,
            Operating = payload?.Operating,
            TemperatureC = payload?.TemperatureC,
            Temperatures = payload?.Temperatures ?? [],
            Statuses = payload?.Statuses ?? [],
        };
    }

    private PowerMpptDetailSnapshotResponse MapMpptDetail(
        string sourceId,
        DataModels.Models.V9.PowerMpptDetailSnapshot? row,
        int staleAfterMinutes,
        PowerMpptDetailPayload? payload,
        DateTime nowUtc)
    {
        if (row is null)
            return new PowerMpptDetailSnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true };

        return new PowerMpptDetailSnapshotResponse
        {
            Id = row.Id,
            SourceId = row.SourceId,
            SourceSystem = row.SourceSystem,
            DeviceId = row.DeviceId,
            RecordedAtUtc = DateTime.SpecifyKind(row.RecordedAt, DateTimeKind.Utc),
            IsPresent = true,
            IsStale = nowUtc - DateTime.SpecifyKind(row.RecordedAt, DateTimeKind.Utc) > TimeSpan.FromMinutes(staleAfterMinutes),
            Trackers = payload?.Trackers ?? [],
            BatteryOutput = payload?.BatteryOutput,
            Temperatures = payload?.Temperatures ?? [],
            Diagnostics = payload?.Diagnostics ?? [],
        };
    }

    private static string NormalizeSourceId(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return sourceId.Trim();
    }

    private DateTime FutureCutoffUtc(DateTime nowUtc) => nowUtc
        .AddSeconds(compositionOptions.Value.MaxFutureClockSkewSeconds);

    private PowerInverterDetailSnapshotResponse? TryMapInverterDetail(
        string sourceId,
        DataModels.Models.V9.PowerInverterDetailSnapshot? row,
        int staleAfterMinutes,
        DateTime nowUtc)
    {
        try
        {
            if (row is null)
                return MapInverterDetail(sourceId, null, staleAfterMinutes, null, nowUtc);
            var payload = JsonSerializer.Deserialize<PowerInverterDetailPayload>(row.PayloadJson, JsonOptions);
            if (payload is null || payload.PvStrings is null || payload.PvStrings.Any(item => item is null)
                || payload.Temperatures is null || payload.Temperatures.Any(item => item is null)
                || payload.Statuses is null || payload.Statuses.Any(item => item is null))
                throw new JsonException("Inverter detail payload is null or has a null collection or entry.");
            return MapInverterDetail(sourceId, row, staleAfterMinutes, payload, nowUtc);
        }
        catch (JsonException exception)
        {
            logger?.LogWarning(exception,
                "Skipping invalid inverter detail snapshot {SnapshotId} for source {SourceId}",
                row?.Id,
                sourceId);
            return null;
        }
    }

    private PowerMpptDetailSnapshotResponse? TryMapMpptDetail(
        string sourceId,
        DataModels.Models.V9.PowerMpptDetailSnapshot? row,
        int staleAfterMinutes,
        DateTime nowUtc)
    {
        try
        {
            if (row is null)
                return MapMpptDetail(sourceId, null, staleAfterMinutes, null, nowUtc);
            var payload = JsonSerializer.Deserialize<PowerMpptDetailPayload>(row.PayloadJson, JsonOptions);
            if (payload?.Trackers is null || payload.Trackers.Any(tracker => tracker is null))
                throw new JsonException("MPPT detail payload has a null tracker collection or entry.");
            return MapMpptDetail(sourceId, row, staleAfterMinutes, payload, nowUtc);
        }
        catch (JsonException exception)
        {
            logger?.LogWarning(exception,
                "Skipping invalid MPPT detail snapshot {SnapshotId} for source {SourceId}",
                row?.Id,
                sourceId);
            return null;
        }
    }

}
