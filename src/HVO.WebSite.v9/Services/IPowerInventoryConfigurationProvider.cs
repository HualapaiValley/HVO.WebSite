using HVO.WebSite.v9.Models;

namespace HVO.WebSite.v9.Services;

public interface IPowerInventoryConfigurationProvider
{
    Task<(PowerDeviceInventorySnapshotResponse Inventory, PowerConfigurationSnapshotResponse Configuration)> GetLatestAsync(
        string sourceId = "solarassistant-total",
        int staleAfterMinutes = 1440,
        CancellationToken ct = default);

    Task<(
        PowerDeviceInventorySnapshotResponse Inventory,
        PowerConfigurationSnapshotResponse Configuration,
        PowerEnergySnapshotResponse Energy,
        PowerInverterDetailSnapshotResponse InverterDetail,
        GatewayStatusSnapshotResponse GatewayStatus)> GetLatestCentralAsync(
        string sourceId = "solarassistant-total",
        int staleAfterMinutes = 1440,
        CancellationToken ct = default);

    Task<PowerInverterDetailSnapshotResponse> GetLatestInverterDetailAsync(
        string sourceId,
        int staleAfterMinutes = 5,
        CancellationToken ct = default);

    Task<PowerMpptDetailSnapshotResponse> GetLatestMpptDetailAsync(
        string sourceId,
        int staleAfterMinutes = 5,
        CancellationToken ct = default);

    Task<PowerTelemetryHistoryResponse> GetRecentTelemetryAsync(
        IReadOnlyCollection<string> mpptSourceIds,
        IReadOnlyCollection<string> batterySourceIds,
        DateTime sinceUtc,
        CancellationToken ct = default);
}
