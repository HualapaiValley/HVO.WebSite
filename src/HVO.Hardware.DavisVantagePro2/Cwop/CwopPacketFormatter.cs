using System.Globalization;
using System.Text;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station.Models;

namespace HVO.Hardware.DavisVantagePro2.Cwop;

internal static class CwopPacketFormatter
{
    public static string FormatLogin(string stationId, string passcode, string softwareName, string softwareVersion) =>
        $"user {stationId} pass {passcode} vers {softwareName} {softwareVersion}";

    public static string FormatPacket(string stationId, Loop2Packet observation, StationSettings settings)
    {
        var latitude = settings.LatitudeDegrees!.Value;
        var longitude = settings.LongitudeDegrees!.Value;
        var timestamp = DateTime.SpecifyKind(observation.RecordedAtUtc, DateTimeKind.Utc);
        var packet = new StringBuilder()
            .Append(stationId).Append(">APRS,TCPIP*:@")
            .Append(timestamp.ToString("ddHHmm'z'", CultureInfo.InvariantCulture))
            .Append(FormatCoordinate(latitude, true)).Append('/')
            .Append(FormatCoordinate(longitude, false)).Append('_')
            // LOOP2 has averaged speed but no averaged direction; use its current direction, not gust direction.
            .Append(ThreeDigits(observation.WindDirectionDegrees, 0, 360)).Append('/')
            .Append(ThreeDigits(observation.WindSpeed2MinAvgMph, 0, 999));

        Append(packet, 'g', ThreeDigits(observation.WindGust10MinMph, 0, 999));
        Append(packet, 't', Temperature(observation.OutsideTemperatureF));
        Append(packet, 'r', RainHundredths(observation.HourRainInches));
        Append(packet, 'p', RainHundredths(observation.Rain24HourInches));
        Append(packet, 'P', RainHundredths(observation.DailyRainInches));
        Append(packet, 'h', Humidity(observation.OutsideHumidityPercent));
        Append(packet, 'b', PressureTenthsMillibar(observation.BarometricPressureInHg));
        AppendSolar(packet, observation.SolarRadiationWm2);
        packet.Append("/A=").Append(((int)Math.Round(settings.AltitudeFeet!.Value, MidpointRounding.AwayFromZero)).ToString("000000", CultureInfo.InvariantCulture));

        if (Encoding.ASCII.GetByteCount(packet.ToString()) + 2 > 512)
            throw new InvalidOperationException("The formatted CWOP packet exceeds the APRS-IS line limit.");
        return packet.ToString();
    }

    public static string? ValidatePosition(StationSettings? settings)
    {
        if (settings?.LatitudeDegrees is not double latitude || !double.IsFinite(latitude) || latitude is < -90 or > 90)
            return "invalid-latitude";
        if (settings.LongitudeDegrees is not double longitude || !double.IsFinite(longitude) || longitude is < -180 or > 180)
            return "invalid-longitude";
        if (settings.AltitudeFeet is not double altitude || !double.IsFinite(altitude) || altitude is < 0 or > 999999)
            return "invalid-elevation";
        return null;
    }

    private static string FormatCoordinate(double value, bool latitude)
    {
        var absolute = Math.Abs(value);
        var degrees = (int)Math.Floor(absolute);
        var minutes = Math.Round((absolute - degrees) * 60, 2, MidpointRounding.AwayFromZero);
        if (minutes >= 60)
        {
            degrees++;
            minutes = 0;
        }
        var hemisphere = latitude ? value >= 0 ? 'N' : 'S' : value >= 0 ? 'E' : 'W';
        return string.Create(CultureInfo.InvariantCulture, $"{degrees.ToString(latitude ? "00" : "000", CultureInfo.InvariantCulture)}{minutes:00.00}{hemisphere}");
    }

    private static string ThreeDigits(double? value, double minimum, double maximum) =>
        value is double number && double.IsFinite(number) && number >= minimum && number <= maximum
            ? ((int)Math.Round(number == 360 ? 0 : number, MidpointRounding.AwayFromZero)).ToString("000", CultureInfo.InvariantCulture)
            : "...";

    private static string Temperature(double? value)
    {
        if (value is not double number || !double.IsFinite(number) || number is < -99 or > 999)
            return "...";
        var rounded = (int)Math.Round(number, MidpointRounding.AwayFromZero);
        return rounded < 0 ? $"-{Math.Abs(rounded):00}" : rounded.ToString("000", CultureInfo.InvariantCulture);
    }

    private static string RainHundredths(double? inches) =>
        inches is double number && double.IsFinite(number) && number is >= 0 and <= 9.99
            ? ((int)Math.Round(number * 100, MidpointRounding.AwayFromZero)).ToString("000", CultureInfo.InvariantCulture)
            : "...";

    private static string Humidity(double? value)
    {
        if (value is not double number || !double.IsFinite(number) || number is < 1 or > 100)
            return "..";
        var rounded = (int)Math.Round(number, MidpointRounding.AwayFromZero);
        return rounded == 100 ? "00" : rounded.ToString("00", CultureInfo.InvariantCulture);
    }

    private static string PressureTenthsMillibar(double? inchesHg)
    {
        if (inchesHg is not double number || !double.IsFinite(number) || number <= 0)
            return ".....";
        var value = (int)Math.Round(number * 33.8638866667 * 10, MidpointRounding.AwayFromZero);
        return value is >= 0 and <= 99999 ? value.ToString("00000", CultureInfo.InvariantCulture) : ".....";
    }

    private static void AppendSolar(StringBuilder packet, double? radiation)
    {
        if (radiation is not double number || !double.IsFinite(number) || number < 0)
            return;
        var rounded = (int)Math.Round(number, MidpointRounding.AwayFromZero);
        if (rounded <= 999)
            packet.Append('L').Append(rounded.ToString("000", CultureInfo.InvariantCulture));
        else if (rounded <= 1999)
            packet.Append('l').Append((rounded - 1000).ToString("000", CultureInfo.InvariantCulture));
    }

    private static void Append(StringBuilder packet, char identifier, string value) => packet.Append(identifier).Append(value);
}
