namespace HVO.Hardware.DavisVantagePro2.Api;

public sealed record CurrentConditionsResponse
{
    public DateTime ObservedAtUtc { get; init; }
    public DateTimeOffset ObservedAtLocal { get; init; }
    public string ConsoleTimeZone { get; init; } = string.Empty;
    public bool IsConnected { get; init; }
    public string TemperatureUnits { get; init; } = "°F";
    public string BarometerUnits { get; init; } = "inHg";
    public string RainUnits { get; init; } = "inch";
    public string WindUnits { get; init; } = "mph";

    public double? OutsideTemperatureF { get; init; }
    public double? OutsideHumidityPercent { get; init; }
    public double? DewPointF { get; init; }
    public double? HeatIndexF { get; init; }
    public double? WindChillF { get; init; }

    public double? InsideTemperatureF { get; init; }
    public double? InsideHumidityPercent { get; init; }

    public double? BarometricPressureInHg { get; init; }
    public double? PressureRawInHg { get; init; }
    public double? AltimeterInHg { get; init; }
    public int? BarometricTrend { get; init; }

    public double? WindSpeedMph { get; init; }
    public double? WindDirectionDegrees { get; init; }
    public double? WindSpeed10MinAvgMph { get; init; }
    public double? WindGust10MinMph { get; init; }
    public double? WindGust10MinDirectionDegrees { get; init; }

    public double? RainRateInchesPerHour { get; init; }
    public double? DailyRainInches { get; init; }
    public double? Rain24HourInches { get; init; }
    public double? StormRainInches { get; init; }
    public double? DailyEtInches { get; init; }
    public double? MonthlyRainInches { get; init; }
    public double? YearlyRainInches { get; init; }

    public double? SolarRadiationWm2 { get; init; }
    public double? UvIndex { get; init; }

    public string? Forecast { get; init; }
    public string? Sunrise { get; init; }
    public string? Sunset { get; init; }

    public double? ConsoleBatteryVoltage { get; init; }
    public IReadOnlyList<int> TransmitterLowBatteryChannels { get; init; } = [];

    public int PendingOutboxCount { get; init; }
    public int FailedOutboxCount { get; init; }

    public DisplayCurrentConditionsResponse? Display { get; init; }
}

public sealed record DisplayCurrentConditionsResponse
{
    public string TemperatureUnits { get; init; } = "°F";
    public string BarometerUnits { get; init; } = "inHg";
    public string RainUnits { get; init; } = "inch";
    public string WindUnits { get; init; } = "mph";

    public double? OutsideTemperature { get; init; }
    public double? DewPoint { get; init; }
    public double? HeatIndex { get; init; }
    public double? WindChill { get; init; }
    public double? InsideTemperature { get; init; }
    public double? BarometricPressure { get; init; }
    public double? PressureRaw { get; init; }
    public double? Altimeter { get; init; }
    public double? WindSpeed { get; init; }
    public double? WindSpeed10MinAvg { get; init; }
    public double? WindGust10Min { get; init; }
    public double? RainRate { get; init; }
    public double? DailyRain { get; init; }
    public double? Rain24Hour { get; init; }
    public double? StormRain { get; init; }
    public double? DailyEt { get; init; }
    public double? MonthlyRain { get; init; }
    public double? YearlyRain { get; init; }
}
