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
        var inventory = await _db.PowerDeviceInventorySnapshots
            .AsNoTracking()
            .Where(r => r.SourceId == normalized)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);
        var configuration = await _db.PowerConfigurationSnapshots
            .AsNoTracking()
            .Where(r => r.SourceId == normalized)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);

        return (MapInventory(normalized, inventory, staleAfterMinutes), MapConfiguration(normalized, configuration, staleAfterMinutes));
    }

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
}
