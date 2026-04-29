namespace HVO.Hardware.DavisVantagePro2.Station.Models;

using HVO.Hardware.DavisVantagePro2.Protocol;

/// <summary>Hardware and firmware identification returned by WRD + NVER + VER commands.</summary>
public sealed record StationInfo
{
    public string HardwareName { get; init; } = string.Empty;
    public int HardwareType { get; init; }
    public int ModelType { get; init; }   // 1=Vantage Pro, 2=Vantage Pro 2
    public string FirmwareVersion { get; init; } = string.Empty;
    public string FirmwareDate { get; init; } = string.Empty;
    public DateTime ConsoleTime { get; init; }

    public string HardwareDescription => (HardwareType, ModelType) switch
    {
        (16, 1) => "Vantage Pro",
        (16, _) => "Vantage Pro 2",
        (17, _) => "Vantage Vue",
        _ => $"Unknown (type={HardwareType})"
    };
}

/// <summary>Console settings stored in EEPROM (archive interval, location, DST, etc.).</summary>
public sealed record StationSettings
{
    public int ArchiveIntervalSeconds { get; init; }
    public double? LatitudeDegrees { get; init; }
    public double? LongitudeDegrees { get; init; }
    public double? AltitudeFeet { get; init; }
    public int RainYearStartMonth { get; init; }  // 1=January
    public int RainBucketType { get; init; }       // 0=0.01in, 1=0.2mm, 2=0.1mm
    public string DstSetting { get; init; } = string.Empty;  // "AUTO", "ON", "OFF"
    public bool UseTimezoneCode { get; init; }
    public int TimezoneCode { get; init; }
    public double GmtOffsetHours { get; init; }
    public string TemperatureLogging { get; init; } = string.Empty; // "LAST" or "AVERAGE"
    public string BarometerUnits { get; init; } = string.Empty;
    public string TemperatureUnits { get; init; } = string.Empty;
    public string RainUnits { get; init; } = string.Empty;
    public string WindUnits { get; init; } = string.Empty;

    public string RainBucketDescription => RainBucketType switch
    {
        0 => "0.01 inches",
        1 => "0.2 mm",
        2 => "0.1 mm",
        _ => $"Unknown ({RainBucketType})"
    };

    public int ArchiveIntervalMinutes => ArchiveIntervalSeconds / 60;

    /// <summary>Human-readable timezone label, e.g. "Mountain (UTC-7)".</summary>
    public string TimeZoneLabel => UseTimezoneCode
        ? DavisTimeZoneTable.GetLabel(TimezoneCode)
        : $"GMT {(GmtOffsetHours >= 0 ? "+" : "")}{GmtOffsetHours:F2} h";

    /// <summary>UTC offset for the console's configured timezone.</summary>
    public TimeSpan UtcOffset => UseTimezoneCode
        ? DavisTimeZoneTable.GetOffset(TimezoneCode) ?? TimeSpan.Zero
        : TimeSpan.FromHours(GmtOffsetHours);
}

/// <summary>Transmitter configuration for one of the eight Davis ISS channels.</summary>
public sealed record TransmitterConfig
{
    public int Channel { get; init; }             // 1–8
    public string TransmitterType { get; init; } = string.Empty; // "iss", "temp", "hum", "temp_hum", "wind", "rain", etc.
    public string? RepeaterId { get; init; }      // "A"–"H" or null
    public bool IsActive { get; init; }
    public bool IsRetransmitting { get; init; }
    public int? ExtraTemperatureId { get; init; } // 1–8, if type is "temp" or "temp_hum"
    public int? ExtraHumidityId { get; init; }    // 1–8, if type is "hum" or "temp_hum"
}

/// <summary>On-board temperature, humidity, and wind direction calibration offsets.</summary>
public sealed record CalibrationData
{
    public double InsideTempOffsetF { get; init; }
    public double OutsideTempOffsetF { get; init; }
    public double InsideHumidOffsetPct { get; init; }
    public double OutsideHumidOffsetPct { get; init; }
    public int WindDirOffsetDegrees { get; init; }
    public IReadOnlyList<double> ExtraTempOffsets { get; init; } = []; // indices 0–6 → extraTemp1–7
    public IReadOnlyList<double> SoilTempOffsets { get; init; } = []; // indices 0–3
    public IReadOnlyList<double> LeafTempOffsets { get; init; } = []; // indices 0–3
    public IReadOnlyList<double> ExtraHumidOffsets { get; init; } = []; // indices 0–6
}

/// <summary>Barometer calibration data returned by the BARDATA command.</summary>
public sealed record BarometerData
{
    public double CurrentPressureInHg { get; init; }
    public double AltitudeFeet { get; init; }
    public double DewPointF { get; init; }
    public double VirtualTemperatureF { get; init; }
    public double CorrectionFactor { get; init; }
    public double CorrectionRatio { get; init; }
    public double CorrectionConstantInHg { get; init; }
    public double Gain { get; init; }
    public double ErrorOffset { get; init; }
}

/// <summary>Reception statistics from the RXCHECK command.</summary>
public sealed record ReceptionStats
{
    public int TotalPacketsReceived { get; init; }
    public int TotalPacketsMissed { get; init; }
    public int NumberOfResynchronizations { get; init; }
    public int LongestGoodStretch { get; init; }
    public int NumberOfCrcErrors { get; init; }
    public double ReceptionPercent => TotalPacketsReceived + TotalPacketsMissed > 0
        ? 100.0 * TotalPacketsReceived / (TotalPacketsReceived + TotalPacketsMissed)
        : 0;
}
