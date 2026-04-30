namespace HVO.Hardware.JkBms.Components;

/// <summary>
/// Decodes the JK BMS alarm bitmask (from <c>CellInfoPacket.AlarmBitmask</c>) into
/// human-readable alarm names for display in the UI.
///
/// Bit definitions are based on the esphome-jk-bms reference implementation (JK02_24S variant).
/// </summary>
internal static class AlarmDecoder
{
    // Known named bits (0-based positions in the uint32 bitmask)
    private static readonly (int Bit, string Name)[] KnownAlarms =
    [
        ( 0, "Charge Overtemperature"),
        ( 1, "Charge Undertemperature"),
        ( 2, "Cell Overvoltage (cell)"),
        ( 3, "Cell Undervoltage"),
        ( 4, "Pack Undervoltage"),
        ( 5, "Discharge Overcurrent"),
        ( 6, "Charge Overcurrent"),
        ( 7, "Discharge Overtemperature"),
        ( 8, "Short Circuit"),
        ( 9, "Discharge Undertemperature"),
        (10, "Charge Overcurrent (protection)"),
        (11, "Cell Overvoltage (protection)"),
        (12, "Cell Overvoltage"),
        (13, "Pack Overvoltage"),
        (14, "Low Capacity"),
        (15, "MOS Overtemperature"),
    ];

    /// <summary>
    /// Returns the alarm names that correspond to the set bits in <paramref name="bitmask"/>.
    /// Unknown bits (16–31) are reported as "Unknown flag (bit N)".
    /// Returns an empty sequence when <paramref name="bitmask"/> is zero.
    /// </summary>
    public static IEnumerable<string> Decode(uint bitmask)
    {
        if (bitmask == 0)
            yield break;

        foreach (var (bit, name) in KnownAlarms)
            if ((bitmask & (1u << bit)) != 0)
                yield return name;

        for (int bit = 16; bit < 32; bit++)
            if ((bitmask & (1u << bit)) != 0)
                yield return $"Unknown flag (bit {bit})";
    }
}
