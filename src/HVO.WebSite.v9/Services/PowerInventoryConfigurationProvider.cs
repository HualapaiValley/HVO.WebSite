using System.Text.Json;
using HVO.DataModels.Data;
using HVO.Edge.Contracts;
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
    private readonly HvoV9DbContext _db = db;

    public PowerInventoryConfigurationProvider(HvoV9DbContext db)
        : this(db, Options.Create(new PowerCompositionOptions()), TimeProvider.System, null) { }

    public async Task<(PowerDeviceInventorySnapshotResponse Inventory, PowerConfigurationSnapshotResponse Configuration)> GetLatestAsync(
        string sourceId = "solarassistant-total",
        int staleAfterMinutes = 1440,
        CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(sourceId) ? "solarassistant-total" : sourceId.Trim();
        var inventory = await GetLatestInventoryRowAsync(normalized, ct);
        var configuration = await GetLatestConfigurationRowAsync(normalized, ct);
        return (MapInventory(normalized, inventory, staleAfterMinutes), MapConfiguration(normalized, configuration, staleAfterMinutes));
    }

    public async Task<(
        PowerDeviceInventorySnapshotResponse Inventory,
        PowerConfigurationSnapshotResponse Configuration,
        PowerEnergySnapshotResponse Energy,
        PowerInverterDetailSnapshotResponse InverterDetail,
        GatewayStatusSnapshotResponse GatewayStatus)> GetLatestCentralAsync(
        string sourceId = "solarassistant-total",
        int staleAfterMinutes = 1440,
        CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(sourceId) ? "solarassistant-total" : sourceId.Trim();
        var futureCutoffUtc = FutureCutoffUtc();
        var inventory = await GetLatestInventoryRowAsync(normalized, ct);
        var configuration = await GetLatestConfigurationRowAsync(normalized, ct);
        var energy = await _db.PowerEnergySnapshots
            .AsNoTracking()
            .Where(r => r.SourceId == normalized && r.RecordedAt <= futureCutoffUtc)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);
        var inverterDetailRows = await _db.PowerInverterDetailSnapshots
            .AsNoTracking()
            .Where(r => r.SourceId == normalized && r.RecordedAt <= futureCutoffUtc)
            .OrderByDescending(r => r.RecordedAt)
            .ThenByDescending(r => r.Id)
            .Take(100)
            .ToArrayAsync(ct);
        var inverterDetail = inverterDetailRows
            .Select(row => TryMapInverterDetail(normalized, row, staleAfterMinutes))
            .OfType<PowerInverterDetailSnapshotResponse>()
            .FirstOrDefault()
            ?? new PowerInverterDetailSnapshotResponse { SourceId = normalized, IsPresent = false, IsStale = true };
        var gatewayStatus = await _db.GatewayStatusSnapshots
            .AsNoTracking()
            .Where(r => r.SourceId == normalized)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);

        return (
            MapInventory(normalized, inventory, staleAfterMinutes),
            MapConfiguration(normalized, configuration, staleAfterMinutes),
            MapEnergy(normalized, energy, staleAfterMinutes),
            inverterDetail,
            MapGatewayStatus(normalized, gatewayStatus, staleAfterMinutes));
    }

    public async Task<PowerInverterDetailSnapshotResponse> GetLatestInverterDetailAsync(
        string sourceId,
        int staleAfterMinutes = 5,
        CancellationToken ct = default)
    {
        var normalized = NormalizeSourceId(sourceId);
        var futureCutoffUtc = FutureCutoffUtc();
        var rows = await _db.PowerInverterDetailSnapshots
            .AsNoTracking()
            .Where(item => item.SourceId == normalized && item.RecordedAt <= futureCutoffUtc)
            .OrderByDescending(item => item.RecordedAt)
            .ThenByDescending(item => item.Id)
            .Take(100)
            .ToArrayAsync(ct);
        return rows.Select(row => TryMapInverterDetail(normalized, row, staleAfterMinutes))
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
        var futureCutoffUtc = FutureCutoffUtc();
        var rows = await _db.PowerMpptDetailSnapshots
            .AsNoTracking()
            .Where(item => item.SourceId == normalized && item.RecordedAt <= futureCutoffUtc)
            .OrderByDescending(item => item.RecordedAt)
            .ThenByDescending(item => item.Id)
            .Take(100)
            .ToArrayAsync(ct);
        return rows.Select(row => TryMapMpptDetail(normalized, row, staleAfterMinutes))
            .OfType<PowerMpptDetailSnapshotResponse>()
            .FirstOrDefault()
            ?? new PowerMpptDetailSnapshotResponse { SourceId = normalized, IsPresent = false, IsStale = true };
    }

    public async Task<PowerTelemetryHistoryResponse> GetRecentTelemetryAsync(
        IReadOnlyCollection<string> mpptSourceIds,
        IReadOnlyCollection<string> batterySourceIds,
        DateTime sinceUtc,
        CancellationToken ct = default)
    {
        var normalizedMppt = mpptSourceIds.Select(NormalizeSourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var normalizedBattery = batterySourceIds.Select(NormalizeSourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var futureCutoffUtc = FutureCutoffUtc();
        var mpptRows = new List<DataModels.Models.V9.PowerMpptDetailSnapshot>();
        foreach (var sourceId in normalizedMppt)
        {
            mpptRows.AddRange(await _db.PowerMpptDetailSnapshots
                .AsNoTracking()
                .Where(row => row.SourceId == sourceId && row.RecordedAt >= sinceUtc && row.RecordedAt <= futureCutoffUtc)
                .OrderByDescending(row => row.RecordedAt)
                .Take(5_000)
                .ToArrayAsync(ct));
        }
        var batteryRows = new List<PowerBatteryHistoryPoint>();
        foreach (var sourceId in normalizedBattery)
        {
            batteryRows.AddRange(await _db.PowerReadings
                .AsNoTracking()
                .Where(row => row.SourceId == sourceId && row.RecordedAt >= sinceUtc && row.RecordedAt <= futureCutoffUtc)
                .OrderByDescending(row => row.RecordedAt)
                .Take(5_000)
                .Select(row => new PowerBatteryHistoryPoint(row.RecordedAt, row.SourceId, row.DeviceId, row.BatteryPowerW))
                .ToArrayAsync(ct));
        }

        return new PowerTelemetryHistoryResponse(
            mpptRows.OrderBy(row => row.RecordedAt).ThenBy(row => row.Id)
                .Select(row => TryMapMpptDetail(row.SourceId, row, staleAfterMinutes: int.MaxValue))
                .OfType<PowerMpptDetailSnapshotResponse>()
                .GroupBy(row => new { SourceId = row.SourceId.ToUpperInvariant(), Bucket = HistoryBucket(row.RecordedAtUtc) })
                .Select(group => group.Last())
                .OrderBy(row => row.RecordedAtUtc)
                .ToArray(),
            batteryRows
                .GroupBy(row => new
                {
                    SourceId = row.SourceId.ToUpperInvariant(),
                    DeviceId = row.DeviceId?.ToUpperInvariant(),
                    Bucket = HistoryBucket(row.RecordedAtUtc),
                })
                .Select(group => group.OrderBy(row => row.RecordedAtUtc).Last())
                .OrderBy(row => row.RecordedAtUtc)
                .ToArray());
    }

    private Task<DataModels.Models.V9.PowerDeviceInventorySnapshot?> GetLatestInventoryRowAsync(string sourceId, CancellationToken ct) =>
        _db.PowerDeviceInventorySnapshots
            .AsNoTracking()
            .Where(r => r.SourceId == sourceId)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);

    private Task<DataModels.Models.V9.PowerConfigurationSnapshot?> GetLatestConfigurationRowAsync(string sourceId, CancellationToken ct) =>
        _db.PowerConfigurationSnapshots
            .AsNoTracking()
            .Where(r => r.SourceId == sourceId)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);

    private static PowerDeviceInventorySnapshotResponse MapInventory(string sourceId, DataModels.Models.V9.PowerDeviceInventorySnapshot? row, int staleAfterMinutes)
    {
        if (row is null)
            return new PowerDeviceInventorySnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true };

        var payload = JsonSerializer.Deserialize<PowerDeviceInventoryPayload>(row.PayloadJson, JsonOptions);
        return new PowerDeviceInventorySnapshotResponse
        {
            SourceId = row.SourceId,
            SourceSystem = row.SourceSystem,
            DeviceId = row.DeviceId,
            RecordedAtUtc = row.RecordedAt,
            IsPresent = true,
            IsStale = DateTime.UtcNow - row.RecordedAt.ToUniversalTime() > TimeSpan.FromMinutes(staleAfterMinutes),
            RestMetricCount = row.RestMetricCount,
            MqttEntityCount = row.MqttEntityCount,
            MqttStateTopicCount = row.MqttStateTopicCount,
            Devices = payload?.Devices ?? [],
        };
    }

    private static PowerConfigurationSnapshotResponse MapConfiguration(string sourceId, DataModels.Models.V9.PowerConfigurationSnapshot? row, int staleAfterMinutes)
    {
        if (row is null)
            return new PowerConfigurationSnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true };

        var payload = JsonSerializer.Deserialize<PowerConfigurationPayload>(row.PayloadJson, JsonOptions);
        return new PowerConfigurationSnapshotResponse
        {
            SourceId = row.SourceId,
            SourceSystem = row.SourceSystem,
            DeviceId = row.DeviceId,
            RecordedAtUtc = row.RecordedAt,
            IsPresent = true,
            IsStale = DateTime.UtcNow - row.RecordedAt.ToUniversalTime() > TimeSpan.FromMinutes(staleAfterMinutes),
            Settings = payload?.Settings ?? [],
            CommandCapabilities = payload?.CommandCapabilities ?? [],
        };
    }

    private static PowerEnergySnapshotResponse MapEnergy(string sourceId, DataModels.Models.V9.PowerEnergySnapshot? row, int staleAfterMinutes)
    {
        if (row is null)
            return new PowerEnergySnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true };

        var payload = JsonSerializer.Deserialize<PowerEnergyPayload>(row.PayloadJson, JsonOptions);
        return new PowerEnergySnapshotResponse
        {
            SourceId = row.SourceId,
            SourceSystem = row.SourceSystem,
            DeviceId = row.DeviceId,
            RecordedAtUtc = row.RecordedAt,
            IsPresent = true,
            IsStale = DateTime.UtcNow - row.RecordedAt.ToUniversalTime() > TimeSpan.FromMinutes(staleAfterMinutes),
            CounterResetDetected = payload?.CounterResetDetected ?? row.CounterResetDetected,
            Counters = payload?.Counters ?? [],
        };
    }

    private static PowerInverterDetailSnapshotResponse MapInverterDetail(
        string sourceId,
        DataModels.Models.V9.PowerInverterDetailSnapshot? row,
        int staleAfterMinutes,
        PowerInverterDetailPayload? payload)
    {
        if (row is null)
            return new PowerInverterDetailSnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true };

        return new PowerInverterDetailSnapshotResponse
        {
            SourceId = row.SourceId,
            SourceSystem = row.SourceSystem,
            DeviceId = row.DeviceId,
            RecordedAtUtc = row.RecordedAt,
            IsPresent = true,
            IsStale = DateTime.UtcNow - row.RecordedAt.ToUniversalTime() > TimeSpan.FromMinutes(staleAfterMinutes),
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

    private static PowerMpptDetailSnapshotResponse MapMpptDetail(
        string sourceId,
        DataModels.Models.V9.PowerMpptDetailSnapshot? row,
        int staleAfterMinutes,
        PowerMpptDetailPayload? payload)
    {
        if (row is null)
            return new PowerMpptDetailSnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true };

        return new PowerMpptDetailSnapshotResponse
        {
            Id = row.Id,
            SourceId = row.SourceId,
            SourceSystem = row.SourceSystem,
            DeviceId = row.DeviceId,
            RecordedAtUtc = row.RecordedAt,
            IsPresent = true,
            IsStale = DateTime.UtcNow - row.RecordedAt.ToUniversalTime() > TimeSpan.FromMinutes(staleAfterMinutes),
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

    private static DateTime HistoryBucket(DateTime value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.Ticks - utc.Ticks % TimeSpan.FromMinutes(5).Ticks;
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private DateTime FutureCutoffUtc() => timeProvider.GetUtcNow().UtcDateTime
        .AddSeconds(compositionOptions.Value.MaxFutureClockSkewSeconds);

    private PowerInverterDetailSnapshotResponse? TryMapInverterDetail(
        string sourceId,
        DataModels.Models.V9.PowerInverterDetailSnapshot? row,
        int staleAfterMinutes)
    {
        try
        {
            if (row is null)
                return MapInverterDetail(sourceId, null, staleAfterMinutes, null);
            var payload = JsonSerializer.Deserialize<PowerInverterDetailPayload>(row.PayloadJson, JsonOptions);
            if (payload is null || payload.PvStrings is null || payload.PvStrings.Any(item => item is null)
                || payload.Temperatures is null || payload.Temperatures.Any(item => item is null)
                || payload.Statuses is null || payload.Statuses.Any(item => item is null))
                throw new JsonException("Inverter detail payload is null or has a null collection or entry.");
            return MapInverterDetail(sourceId, row, staleAfterMinutes, payload);
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
        int staleAfterMinutes)
    {
        try
        {
            if (row is null)
                return MapMpptDetail(sourceId, null, staleAfterMinutes, null);
            var payload = JsonSerializer.Deserialize<PowerMpptDetailPayload>(row.PayloadJson, JsonOptions);
            if (payload?.Trackers is null || payload.Trackers.Any(tracker => tracker is null))
                throw new JsonException("MPPT detail payload has a null tracker collection or entry.");
            return MapMpptDetail(sourceId, row, staleAfterMinutes, payload);
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

    private static GatewayStatusSnapshotResponse MapGatewayStatus(string sourceId, DataModels.Models.V9.GatewayStatusSnapshot? row, int staleAfterMinutes)
    {
        if (row is null)
            return new GatewayStatusSnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true };

        var recordedAtUtc = DateTime.SpecifyKind(row.RecordedAt, DateTimeKind.Utc);
        var payload = JsonSerializer.Deserialize<GatewayStatusPayload>(row.PayloadJson, JsonOptions);
        return new GatewayStatusSnapshotResponse
        {
            SourceId = row.SourceId,
            SourceSystem = row.SourceSystem,
            DeviceId = row.DeviceId,
            RecordedAtUtc = recordedAtUtc,
            IsPresent = true,
            IsStale = DateTime.UtcNow - recordedAtUtc > TimeSpan.FromMinutes(staleAfterMinutes),
            Identity = payload?.Identity,
            Health = payload?.Health,
            Rest = payload?.Rest,
            Mqtt = payload?.Mqtt,
            Outbox = payload?.Outbox,
            RestMetricCount = payload?.RestMetricCount,
            MqttEntityCount = payload?.MqttEntityCount,
            MqttStateTopicCount = payload?.MqttStateTopicCount,
            MqttCommandTopicCount = payload?.MqttCommandTopicCount,
        };
    }
}
