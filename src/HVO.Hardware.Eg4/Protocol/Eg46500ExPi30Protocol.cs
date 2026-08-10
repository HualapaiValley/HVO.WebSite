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
    Pv2Status,
    ParallelStatus,
    ExtendedStatus,
}

public sealed record Eg46500ExGeneralStatus(
    double AcInputVoltageV,
    double AcInputFrequencyHz,
    double AcOutputVoltageV,
    double AcOutputFrequencyHz,
    int LoadApparentPowerVa,
    int LoadActivePowerW,
    int LoadPercentage,
    int BusVoltageV,
    double BatteryVoltageV,
    int BatteryChargingCurrentA,
    int ReportedStateOfChargePercent,
    int InverterTemperatureC,
    double Pv1CurrentA,
    double Pv1VoltageV,
    double SccBatteryVoltageV,
    int BatteryDischargingCurrentA,
    string StatusFlags,
    int BatteryVoltageOffset,
    int EepromVersion,
    int Pv1PowerW,
    string SecondaryStatusFlags)
{
    public double VoltageV => BatteryVoltageV;
    public int ChargingCurrentA => BatteryChargingCurrentA;
    public int DischargingCurrentA => BatteryDischargingCurrentA;
}

public sealed record Eg46500ExParallelStatus(
    string OperatingMode,
    string FaultCode,
    int TotalLoadApparentPowerVa,
    int TotalLoadActivePowerW,
    int TotalLoadPercentage,
    string StatusFlags,
    string OutputMode,
    string ChargerSourcePriority,
    double Pv2VoltageV,
    int Pv2CurrentA);

public sealed record Eg46500ExPv2Status(
    double CurrentA,
    double VoltageV,
    int PowerW);

public sealed record Eg46500ExExtendedStatus(
    int SccPwmTemperatureC,
    int InverterTemperatureC,
    int BatteryChannelTemperatureC,
    int TransformerTemperatureC,
    int ParallelRole,
    bool FanLocked,
    int FanPwmPercent,
    int Pv1ChargePowerW,
    string ParallelWarningFlags,
    string ChargeStage,
    IReadOnlyList<string> Fields);

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
            [Eg46500ExInquiry.Pv2Status] = "QPIGS2",
            [Eg46500ExInquiry.ParallelStatus] = "QPGS0",
            [Eg46500ExInquiry.ExtendedStatus] = "Q1",
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

    public static Eg46500ExGeneralStatus DecodeGeneralStatus(ReadOnlySpan<byte> frame)
    {
        var fields = DecodePayload(frame).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 21)
            throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, $"Expected 21 QPIGS fields, received {fields.Length}.");
        try
        {
            RequireFormats(fields,
                "3.1", "2.1", "3.1", "2.1", "4d", "4d", "3d", "3d", "2.2", "3d", "3d",
                "4d", "2.1", "3.1", "2.2", "5d", "8b", "2d", "2d", "5d", "3d");
            var status = new Eg46500ExGeneralStatus(
                Decimal(fields[0]), Decimal(fields[1]), Decimal(fields[2]), Decimal(fields[3]),
                Integer(fields[4]), Integer(fields[5]), Integer(fields[6]), Integer(fields[7]),
                Decimal(fields[8]), Integer(fields[9]), Integer(fields[10]), Integer(fields[11]),
                Decimal(fields[12]), Decimal(fields[13]), Decimal(fields[14]), Integer(fields[15]),
                fields[16], Integer(fields[17]), Integer(fields[18]), Integer(fields[19]), fields[20]);
            if (status.ReportedStateOfChargePercent is < 0 or > 100)
                throw new FormatException("QPIGS battery voltage or state of charge is outside its documented range.");
            return status;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, $"QPIGS battery fields are malformed: {exception.Message}");
        }
    }

    public static Eg46500ExParallelStatus DecodeParallelStatus(ReadOnlySpan<byte> frame)
    {
        var fields = DecodePayload(frame).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 29)
            throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, $"Expected 29 QPGS0 fields, received {fields.Length}.");
        try
        {
            RequireFormats(fields,
                "1d", "14a", "1a", "2d", "3.1", "2.2", "3.1", "2.2", "4d", "4d", "3d",
                "2.1", "3d", "3d", "3.1", "3d", "5d", "5d", "3d", "8b", "1d", "1d",
                "3d", "3d", "3d", "2d", "3d", "3.1", "2d");
            return new Eg46500ExParallelStatus(
                fields[2], fields[3], Integer(fields[16]), Integer(fields[17]), Integer(fields[18]),
                fields[19], fields[20], fields[21], Decimal(fields[27]), Integer(fields[28]));
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, $"QPGS0 fields are malformed: {exception.Message}");
        }
    }

    public static Eg46500ExPv2Status DecodePv2Status(ReadOnlySpan<byte> frame)
    {
        var fields = DecodePayload(frame).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 3)
            throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, $"Expected 3 QPIGS2 fields, received {fields.Length}.");
        try
        {
            RequireFormats(fields, "2.1", "3.1", "5d");
            return new Eg46500ExPv2Status(Decimal(fields[0]), Decimal(fields[1]), Integer(fields[2]));
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, $"QPIGS2 fields are malformed: {exception.Message}");
        }
    }

    public static Eg46500ExExtendedStatus DecodeExtendedStatus(ReadOnlySpan<byte> frame)
    {
        var fields = DecodePayload(frame).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is not (17 or 27))
            throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, $"Expected 17 or 27 Q1 fields, received {fields.Length}.");
        try
        {
            var formats = new[]
            {
                "5d", "5d", "2d", "2d", "2d", "3d", "3d", "3d", "3d", "2d", "2d", "3d", "4d",
                "4d", "4d", "2.2", "2d", "1d", "3d", "3d", "3d", "3d", "2.2", "3d", "3d", "1d", "4d",
            };
            RequireFormats(fields, formats[..fields.Length]);
            return new Eg46500ExExtendedStatus(
                Integer(fields[5]), Integer(fields[6]), Integer(fields[7]), Integer(fields[8]),
                Integer(fields[9]), fields[10] != "00", Integer(fields[12]), Integer(fields[13]),
                fields[14], ChargeStage(fields[16]), Array.AsReadOnly(fields));
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, $"Q1 fields are malformed: {exception.Message}");
        }
    }

    private static string ChargeStage(string value) => value switch
    {
        "10" => "none",
        "11" => "bulk",
        "12" => "absorb",
        "13" => "float",
        _ => $"unknown-{value}",
    };

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

    private static double Decimal(string value) => double.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    private static int Integer(string value) => int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);

    private static void RequireFormats(IReadOnlyList<string> fields, params string[] formats)
    {
        if (fields.Count != formats.Length) throw new FormatException("Field format definition does not match payload.");
        for (var index = 0; index < fields.Count; index++)
        {
            var format = formats[index];
            var valid = format[^1] switch
            {
                'd' => IsDigits(fields[index], int.Parse(format[..^1], CultureInfo.InvariantCulture)),
                'b' => IsDigits(fields[index], int.Parse(format[..^1], CultureInfo.InvariantCulture)) &&
                       fields[index].All(character => character is '0' or '1'),
                'a' => fields[index].Length == int.Parse(format[..^1], CultureInfo.InvariantCulture) &&
                       fields[index].All(character => char.IsAsciiLetterOrDigit(character)),
                _ when format.Contains('.') => IsFixedDecimal(
                    fields[index],
                    int.Parse(format[..format.IndexOf('.')], CultureInfo.InvariantCulture),
                    int.Parse(format[(format.IndexOf('.') + 1)..], CultureInfo.InvariantCulture)),
                _ => false,
            };
            if (!valid) throw new FormatException($"Field {index} does not match format {format}.");
        }
    }
}
