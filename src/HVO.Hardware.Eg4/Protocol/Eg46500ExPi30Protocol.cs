using System.Globalization;
using System.Text;

namespace HVO.Hardware.Eg4.Protocol;

public enum Eg46500ExInquiry
{
    ProtocolId,
    ModelName,
    GeneralModelName,
    MainFirmware,
    SecondaryFirmware,
    GeneralStatus,
}

public sealed record Eg46500ExBatteryStatus(
    double VoltageV,
    int ChargingCurrentA,
    int DischargingCurrentA,
    int ReportedStateOfChargePercent);

public static class Eg46500ExPi30Protocol
{
    private static readonly IReadOnlyDictionary<Eg46500ExInquiry, string> Commands =
        new Dictionary<Eg46500ExInquiry, string>
        {
            [Eg46500ExInquiry.ProtocolId] = "QPI",
            [Eg46500ExInquiry.ModelName] = "QMN",
            [Eg46500ExInquiry.GeneralModelName] = "QGMN",
            [Eg46500ExInquiry.MainFirmware] = "QVFW",
            [Eg46500ExInquiry.SecondaryFirmware] = "QVFW3",
            [Eg46500ExInquiry.GeneralStatus] = "QPIGS",
        };

    public static byte[] Encode(Eg46500ExInquiry inquiry)
    {
        if (!Commands.TryGetValue(inquiry, out var command))
            throw new ArgumentOutOfRangeException(nameof(inquiry));
        var payload = Encoding.ASCII.GetBytes(command);
        var crc = ComputeAdjustedCrc(payload);
        return [.. payload, crc.High, crc.Low, 0x0D];
    }

    public static string DecodePayload(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 5 || frame[^1] != 0x0D)
            throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, "PI30 response is truncated or lacks CR framing.");
        var payload = frame[..^3];
        var expected = ComputeAdjustedCrc(payload);
        if (frame[^3] != expected.High || frame[^2] != expected.Low)
            throw new Eg4TransportException(Eg4TransportFailureKind.Crc, "PI30 response CRC is invalid.");
        if (payload.ContainsAnyExceptInRange((byte)0x20, (byte)0x7E))
            throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, "PI30 response contains non-ASCII payload bytes.");
        var text = Encoding.ASCII.GetString(payload);
        if (text == "(NAK") throw new Eg4TransportException(Eg4TransportFailureKind.Protocol, "The inverter rejected the inquiry.");
        if (!text.StartsWith('('))
            throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, "PI30 response lacks its opening marker.");
        return text[1..];
    }

    public static Eg46500ExBatteryStatus DecodeGeneralStatus(ReadOnlySpan<byte> frame)
    {
        var fields = DecodePayload(frame).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 21)
            throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, $"Expected 21 QPIGS fields, received {fields.Length}.");
        try
        {
            if (!IsFixedDecimal(fields[8], 2, 2) || !IsDigits(fields[9], 3) ||
                !IsDigits(fields[10], 3) || !IsDigits(fields[15], 5))
                throw new FormatException("QPIGS battery fields do not match the documented fixed widths.");
            var status = new Eg46500ExBatteryStatus(
                double.Parse(fields[8], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
                int.Parse(fields[9], NumberStyles.None, CultureInfo.InvariantCulture),
                int.Parse(fields[15], NumberStyles.None, CultureInfo.InvariantCulture),
                int.Parse(fields[10], NumberStyles.None, CultureInfo.InvariantCulture));
            if (!double.IsFinite(status.VoltageV) || status.ReportedStateOfChargePercent is < 0 or > 100)
                throw new FormatException("QPIGS battery voltage or state of charge is outside its documented range.");
            return status;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, $"QPIGS battery fields are malformed: {exception.Message}");
        }
    }

    private static (byte High, byte Low) ComputeAdjustedCrc(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0;
        foreach (var value in bytes)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return (Adjust((byte)(crc >> 8)), Adjust((byte)crc));
    }

    private static byte Adjust(byte value) => value is 0x28 or 0x0D or 0x0A or 0x00 ? (byte)(value + 1) : value;

    private static bool IsDigits(string value, int length) =>
        value.Length == length && value.All(character => character is >= '0' and <= '9');

    private static bool IsFixedDecimal(string value, int wholeDigits, int decimalDigits) =>
        value.Length == wholeDigits + decimalDigits + 1 && value[wholeDigits] == '.' &&
        value.Where((_, index) => index != wholeDigits).All(character => character is >= '0' and <= '9');
}
