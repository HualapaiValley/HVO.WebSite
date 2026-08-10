namespace HVO.Hardware.Eg4.Protocol;

public static class Eg4Mppt10048HvProtocol
{
    public const byte UnitId = 1;
    public const ushort StartAddress = 200;
    public const ushort RegisterCount = 18;
    public const byte ReadHoldingRegistersFunction = 0x03;

    public static Eg4ReadRegistersRequest Request { get; } =
        new(UnitId, Eg4RegisterTable.Holding, StartAddress, RegisterCount);

    public static byte[] EncodeRequest(Eg4ReadRegistersRequest request)
    {
        RequireFixedRequest(request);
        var frame = new byte[]
        {
            UnitId,
            ReadHoldingRegistersFunction,
            (byte)(StartAddress >> 8),
            (byte)StartAddress,
            (byte)(RegisterCount >> 8),
            (byte)RegisterCount,
            0,
            0,
        };
        AppendCrc(frame);
        return frame;
    }

    public static Eg4ReadRegistersResponse DecodeResponse(
        Eg4ReadRegistersRequest request,
        ReadOnlySpan<byte> frame)
    {
        RequireFixedRequest(request);
        const int expectedByteCount = RegisterCount * 2;
        const int expectedLength = 3 + expectedByteCount + 2;
        if (frame.Length != expectedLength)
            throw Malformed($"Expected a {expectedLength}-byte MPPT response, received {frame.Length}.");
        if (frame[0] != UnitId)
            throw Malformed($"Expected MPPT unit {UnitId}, received {frame[0]}.");
        if (frame[1] != ReadHoldingRegistersFunction)
            throw new Eg4TransportException(Eg4TransportFailureKind.Protocol, $"Expected Modbus function 0x03, received 0x{frame[1]:X2}.");
        if (frame[2] != expectedByteCount)
            throw Malformed($"Expected MPPT byte count {expectedByteCount}, received {frame[2]}.");
        if (!HasValidCrc(frame))
            throw new Eg4TransportException(Eg4TransportFailureKind.Crc, "MPPT response CRC is invalid.");

        var registers = new ushort[RegisterCount];
        for (var index = 0; index < registers.Length; index++)
            registers[index] = (ushort)((frame[3 + index * 2] << 8) | frame[4 + index * 2]);
        return new Eg4ReadRegistersResponse(registers);
    }

    internal static bool HasValidCrc(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 4) return false;
        var crc = ComputeCrc(frame[..^2]);
        return frame[^2] == (byte)crc && frame[^1] == (byte)(crc >> 8);
    }

    internal static void RequireFixedRequest(Eg4ReadRegistersRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request != Request)
            throw new Eg4TransportException(
                Eg4TransportFailureKind.Protocol,
                "The MPPT production transport permits only unit 1 holding registers 200-217.");
    }

    private static void AppendCrc(Span<byte> frame)
    {
        var crc = ComputeCrc(frame[..^2]);
        frame[^2] = (byte)crc;
        frame[^1] = (byte)(crc >> 8);
    }

    private static ushort ComputeCrc(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
        }
        return crc;
    }

    private static Eg4TransportException Malformed(string message) =>
        new(Eg4TransportFailureKind.MalformedFrame, message);
}
