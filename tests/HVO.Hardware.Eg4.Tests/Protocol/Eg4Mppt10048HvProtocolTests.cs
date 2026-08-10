using FluentAssertions;
using HVO.Hardware.Eg4.Protocol;
using System.IO.Ports;

namespace HVO.Hardware.Eg4.Tests.Protocol;

[TestClass]
public sealed class Eg4Mppt10048HvProtocolTests
{
    [TestMethod]
    public void EncodeRequest_IsExactlyFixedUnitOneFunctionThreeRange()
    {
        Eg4Mppt10048HvProtocol.EncodeRequest(Eg4Mppt10048HvProtocol.Request)
            .Should().Equal(0x01, 0x03, 0x00, 0xC8, 0x00, 0x12, 0x44, 0x39);

        var arbitrary = new Eg4ReadRegistersRequest(1, Eg4RegisterTable.Holding, 201, 18);
        FluentActions.Invoking(() => Eg4Mppt10048HvProtocol.EncodeRequest(arbitrary))
            .Should().Throw<Eg4TransportException>().Which.Kind.Should().Be(Eg4TransportFailureKind.Protocol);
    }

    [TestMethod]
    public void ProductionSerialTransport_IsFixedAt9600EightNOne()
    {
        Eg4Mppt10048HvSerialTransport.BaudRate.Should().Be(9600);
        Eg4Mppt10048HvSerialTransport.DataBits.Should().Be(8);
        Eg4Mppt10048HvSerialTransport.SerialParity.Should().Be(Parity.None);
        Eg4Mppt10048HvSerialTransport.SerialStopBits.Should().Be(StopBits.One);
    }

    [TestMethod]
    public void DecodeResponse_StrictlyValidatesUnitFunctionByteCountLengthAndCrc()
    {
        var valid = Response(Enumerable.Range(0, 18).Select(value => (ushort)value).ToArray());
        Eg4Mppt10048HvProtocol.DecodeResponse(Eg4Mppt10048HvProtocol.Request, valid).Registers
            .Should().Equal(Enumerable.Range(0, 18).Select(value => (ushort)value));

        AssertFailure(Change(valid, 0, 2), Eg4TransportFailureKind.MalformedFrame);
        AssertFailure(Change(valid, 1, 4), Eg4TransportFailureKind.Protocol);
        AssertFailure(Change(valid, 2, 34), Eg4TransportFailureKind.MalformedFrame);
        AssertFailure(valid[..^1], Eg4TransportFailureKind.MalformedFrame);
        AssertFailure(Change(valid, valid.Length - 1, (byte)(valid[^1] ^ 1)), Eg4TransportFailureKind.Crc);
    }

    internal static byte[] Response(IReadOnlyList<ushort> registers)
    {
        var frame = new byte[3 + registers.Count * 2 + 2];
        frame[0] = 1;
        frame[1] = 3;
        frame[2] = checked((byte)(registers.Count * 2));
        for (var index = 0; index < registers.Count; index++)
        {
            frame[3 + index * 2] = (byte)(registers[index] >> 8);
            frame[4 + index * 2] = (byte)registers[index];
        }
        ushort crc = 0xFFFF;
        foreach (var value in frame.AsSpan(0, frame.Length - 2))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
        }
        frame[^2] = (byte)crc;
        frame[^1] = (byte)(crc >> 8);
        return frame;
    }

    private static byte[] Change(byte[] frame, int index, byte value)
    {
        var changed = frame.ToArray();
        changed[index] = value;
        return changed;
    }

    private static void AssertFailure(byte[] frame, Eg4TransportFailureKind kind) =>
        FluentActions.Invoking(() => Eg4Mppt10048HvProtocol.DecodeResponse(Eg4Mppt10048HvProtocol.Request, frame))
            .Should().Throw<Eg4TransportException>().Which.Kind.Should().Be(kind);
}
