using System.Text.Json;
using HVO.DataModels.Data;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Models;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.v9.Services;

public sealed class PowerInventoryConfigurationProvider(HvoV9DbContext db) : IPowerInventoryConfigurationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HvoV9DbContext _db = db;

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
        PowerInverterDetailSnapshotResponse InverterDetail)> GetLatestCentralAsync(
        string sourceId = "solarassistant-total",
        int staleAfterMinutes = 1440,
        CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(sourceId) ? "solarassistant-total" : sourceId.Trim();
        var inventory = await GetLatestInventoryRowAsync(normalized, ct);
        var configuration = await GetLatestConfigurationRowAsync(normalized, ct);
        var energy = await _db.PowerEnergySnapshots
            .AsNoTracking()
            .Where(r => r.SourceId == normalized)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);
        var inverterDetail = await _db.PowerInverterDetailSnapshots
            .AsNoTracking()
            .Where(r => r.SourceId == normalized)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);

        return (
            MapInventory(normalized, inventory, staleAfterMinutes),
            MapConfiguration(normalized, configuration, staleAfterMinutes),
            MapEnergy(normalized, energy, staleAfterMinutes),
            MapInverterDetail(normalized, inverterDetail, staleAfterMinutes));
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

    private static PowerInverterDetailSnapshotResponse MapInverterDetail(string sourceId, DataModels.Models.V9.PowerInverterDetailSnapshot? row, int staleAfterMinutes)
    {
        if (row is null)
            return new PowerInverterDetailSnapshotResponse { SourceId = sourceId, IsPresent = false, IsStale = true };

        var payload = JsonSerializer.Deserialize<PowerInverterDetailPayload>(row.PayloadJson, JsonOptions);
        return new PowerInverterDetailSnapshotResponse
        {
            SourceId = row.SourceId,
            SourceSystem = row.SourceSystem,
            DeviceId = row.DeviceId,
            RecordedAtUtc = row.RecordedAt,
            IsPresent = true,
            IsStale = DateTime.UtcNow - row.RecordedAt.ToUniversalTime() > TimeSpan.FromMinutes(staleAfterMinutes),
            PvStrings = payload?.PvStrings ?? [],
            Load = payload?.Load,
            Battery = payload?.Battery,
            TemperatureC = payload?.TemperatureC,
            Statuses = payload?.Statuses ?? [],
        };
    }
}
