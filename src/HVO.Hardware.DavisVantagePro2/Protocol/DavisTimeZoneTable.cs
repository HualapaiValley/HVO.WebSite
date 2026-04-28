namespace HVO.Hardware.DavisVantagePro2.Protocol;

/// <summary>
/// Davis Vantage Pro 2 timezone code lookup table.
/// Based on Davis Serial Communications Reference Manual v2.6, Table 4-7.
/// </summary>
public static class DavisTimeZoneTable
{
    private static readonly (string Name, int OffsetMinutes)[] Entries =
    [
        ("Dateline",         -720),  //  0  UTC-12
        ("Samoa",            -660),  //  1  UTC-11
        ("Hawaii",           -600),  //  2  UTC-10
        ("Alaska",           -540),  //  3  UTC-9
        ("Pacific",          -480),  //  4  UTC-8
        ("Mountain",         -420),  //  5  UTC-7
        ("Central",          -360),  //  6  UTC-6
        ("Eastern",          -300),  //  7  UTC-5
        ("Atlantic",         -240),  //  8  UTC-4
        ("Newfoundland",     -210),  //  9  UTC-3:30
        ("E. South America", -180),  // 10  UTC-3
        ("Mid-Atlantic",     -120),  // 11  UTC-2
        ("Azores",            -60),  // 12  UTC-1
        ("GMT",                 0),  // 13  UTC
        ("Central Europe",    +60),  // 14  UTC+1
        ("South Africa",     +120),  // 15  UTC+2
        ("Arab",             +180),  // 16  UTC+3
        ("Iran",             +210),  // 17  UTC+3:30
        ("Arabian",          +240),  // 18  UTC+4
        ("Afghanistan",      +270),  // 19  UTC+4:30
        ("West Asia",        +300),  // 20  UTC+5
        ("India",            +330),  // 21  UTC+5:30
        ("Central Asia",     +360),  // 22  UTC+6
        ("Myanmar",          +390),  // 23  UTC+6:30
        ("SE Asia",          +420),  // 24  UTC+7
        ("China",            +480),  // 25  UTC+8
        ("Tokyo",            +540),  // 26  UTC+9
        ("Cen. Australia",   +570),  // 27  UTC+9:30
        ("AUS Eastern",      +600),  // 28  UTC+10
        ("Central Pacific",  +660),  // 29  UTC+11
        ("New Zealand",      +720),  // 30  UTC+12
        ("Tonga",            +780),  // 31  UTC+13
    ];

    /// <summary>Total number of defined timezone codes (0–31).</summary>
    public static int Count => Entries.Length;

    /// <summary>Returns the timezone name for the given code, or <c>null</c> if unknown.</summary>
    public static string? GetName(int code) =>
        code >= 0 && code < Entries.Length ? Entries[code].Name : null;

    /// <summary>Returns the UTC offset for the given code, or <c>null</c> if unknown.</summary>
    public static TimeSpan? GetOffset(int code) =>
        code >= 0 && code < Entries.Length
            ? TimeSpan.FromMinutes(Entries[code].OffsetMinutes)
            : null;

    /// <summary>
    /// Returns a human-readable label such as "Mountain (UTC-7:00)" for a timezone code,
    /// or "Code {n}" if the code is not in the table.
    /// </summary>
    public static string GetLabel(int code)
    {
        if (code < 0 || code >= Entries.Length)
            return $"Code {code}";

        var (name, minutes) = Entries[code];
        var offset = TimeSpan.FromMinutes(minutes);
        string sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        string offsetStr = abs.Minutes == 0
            ? $"UTC{sign}{(int)abs.TotalHours}"
            : $"UTC{sign}{(int)abs.TotalHours}:{abs.Minutes:D2}";
        return $"{name} ({offsetStr})";
    }
}
