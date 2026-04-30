namespace HVO.Hardware.JkBms.Bms;

/// <summary>
/// Outbox payload sent to the cloud API for a single BMS poll cycle.
///
/// <see cref="Config"/> and <see cref="DeviceInfo"/> are null in steady state and only
/// populated when the hardware app detects that the values have changed since the last
/// successfully forwarded record (hash-based change detection).  The server performs an
/// idempotent upsert so it is safe to re-send them.
/// </summary>
public sealed class BmsIngressRecord
{
    /// <summary>Core pack-level reading (always present).</summary>
    public BmsDeviceReading Reading { get; init; } = null!;

    /// <summary>
    /// Device configuration snapshot. Non-null only when config changed since last send.
    /// </summary>
    public BmsConfigPayload? Config { get; init; }

    /// <summary>
    /// Device info snapshot. Non-null only when device info changed since last send.
    /// </summary>
    public BmsDeviceInfoPayload? DeviceInfo { get; init; }
}
