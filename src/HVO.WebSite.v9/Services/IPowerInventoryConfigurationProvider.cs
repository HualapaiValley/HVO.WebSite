using HVO.WebSite.v9.Models;

namespace HVO.WebSite.v9.Services;

public interface IPowerInventoryConfigurationProvider
{
    Task<(PowerDeviceInventorySnapshotResponse Inventory, PowerConfigurationSnapshotResponse Configuration)> GetLatestAsync(
        string sourceId = "solarassistant-total",
        int staleAfterMinutes = 1440,
        CancellationToken ct = default);
}
