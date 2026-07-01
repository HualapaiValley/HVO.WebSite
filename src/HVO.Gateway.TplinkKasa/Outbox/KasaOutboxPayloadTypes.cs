using HVO.Edge.Contracts;

namespace HVO.Gateway.TplinkKasa.Outbox;

/// <summary>
/// Payload type constants for TP-Link Kasa outbox records.
/// These delegate to the centralized <see cref="EdgePayloadTypes.Legacy"/> registry.
/// </summary>
public static class KasaOutboxPayloadTypes
{
    public const string Energy = EdgePayloadTypes.Legacy.KasaEnergy;
    public const string EnergyVersion = "1";
    public const string Inventory = EdgePayloadTypes.Legacy.KasaInventory;
    public const string InventoryVersion = "1";
}
