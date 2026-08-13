using System.Text.Json;
using System.Text;
using FluentAssertions;
using HVO.Hardware.Eg4.Protocol;

namespace HVO.Hardware.Eg4.Tests.Protocol;

[TestClass]
public sealed class Eg46500ExPi30ProtocolTests
{
    [TestMethod]
    public void Encode_UsesCapturedQpigsCrcAndExposesOnlyInquiries()
    {
        Eg46500ExPi30Protocol.Encode(Eg46500ExInquiry.GeneralStatus).Should().Equal(0x51, 0x50, 0x49, 0x47, 0x53, 0xB7, 0xA9, 0x0D);
        Eg46500ExPi30Protocol.Encode(Eg46500ExInquiry.Pv2Status).Should().Equal(0x51, 0x50, 0x49, 0x47, 0x53, 0x32, 0x68, 0x2D, 0x0D);
        Eg46500ExPi30Protocol.Encode(Eg46500ExInquiry.TotalPvEnergy).Should().Equal(0x51, 0x45, 0x54, 0x81, 0xB6, 0x0D);
        Eg46500ExPi30Protocol.Encode(Eg46500ExInquiry.TotalLoadEnergy).Should().Equal(0x51, 0x4C, 0x54, 0x3B, 0x2E, 0x0D);
        Enum.GetNames<Eg46500ExInquiry>().Should().HaveCount(11)
            .And.NotContain(name => name.Contains("Write", StringComparison.OrdinalIgnoreCase) || name.Contains("Set", StringComparison.OrdinalIgnoreCase));
        typeof(Eg46500ExPi30Protocol).GetMethods().Select(method => method.Name)
            .Should().NotContain(name => name.Contains("Write", StringComparison.OrdinalIgnoreCase) || name.Contains("Set", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void EnergyCounter_DecodesEightDigitWhAndRejectsOtherShapes()
    {
        Eg46500ExPi30Protocol.DecodeEnergyWh(FrameResponse("(00123456")).Should().Be(123456);

        FluentActions.Invoking(() => Eg46500ExPi30Protocol.DecodeEnergyWh(FrameResponse("(1234")))
            .Should().Throw<Eg4TransportException>().Which.Kind.Should().Be(Eg4TransportFailureKind.MalformedFrame);
        FluentActions.Invoking(() => Eg46500ExPi30Protocol.DecodeEnergyWh(FrameResponse("(00012ABC")))
            .Should().Throw<Eg4TransportException>().Which.Kind.Should().Be(Eg4TransportFailureKind.MalformedFrame);
    }

    [TestMethod]
    public void SanitizedLivePayloads_StrictlyDecodeQpigsQpgs0AndQ1WithoutExposingSerial()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "6500ex", "status-2026-08-10-live-sanitized.json")));
        var root = fixture.RootElement;

        var general = Eg46500ExPi30Protocol.DecodeGeneralStatus(FrameResponse("(" + root.GetProperty("qpigs").GetString()));
        var parallel = Eg46500ExPi30Protocol.DecodeParallelStatus(FrameResponse("(" + root.GetProperty("qpgs0").GetString()));
        var extended = Eg46500ExPi30Protocol.DecodeExtendedStatus(FrameResponse("(" + root.GetProperty("q1").GetString()));

        general.Pv1VoltageV.Should().Be(320.9);
        general.Pv1CurrentA.Should().Be(1.7);
        general.Pv1PowerW.Should().Be(549);
        parallel.Pv2VoltageV.Should().Be(335.1);
        parallel.Pv2CurrentA.Should().Be(1);
        extended.SccPwmTemperatureC.Should().Be(57);
        extended.InverterTemperatureC.Should().Be(57);
        extended.BatteryChannelTemperatureC.Should().Be(47);
        extended.TransformerTemperatureC.Should().Be(53);
        extended.ParallelRole.Should().Be(2);
        extended.FanLocked.Should().BeFalse();
        extended.FanPwmPercent.Should().Be(35);
        extended.Pv1ChargePowerW.Should().Be(552);
        extended.ParallelWarningFlags.Should().Be("0000");
        extended.ChargeStage.Should().Be("bulk");
        parallel.ToString().Should().NotContain("00000000000000");
    }

    [TestMethod]
    public void Qpigs2_DecodesOfficial7972ThreeFieldShape()
    {
        var status = Eg46500ExPi30Protocol.DecodePv2Status(FrameResponse("(03.1 327.3 01026"));

        status.Should().Be(new Eg46500ExPv2Status(3.1, 327.3, 1026));
    }

    [TestMethod]
    public void ParallelAndExtendedStatus_RejectWrongFieldCountsAndMalformedWidths()
    {
        FluentActions.Invoking(() => Eg46500ExPi30Protocol.DecodeParallelStatus(FrameResponse("(1 2")))
            .Should().Throw<Eg4TransportException>().Which.Kind.Should().Be(Eg4TransportFailureKind.MalformedFrame);
        FluentActions.Invoking(() => Eg46500ExPi30Protocol.DecodeExtendedStatus(FrameResponse("(1 2")))
            .Should().Throw<Eg4TransportException>().Which.Kind.Should().Be(Eg4TransportFailureKind.MalformedFrame);
        FluentActions.Invoking(() => Eg46500ExPi30Protocol.DecodePv2Status(FrameResponse("(1 2")))
            .Should().Throw<Eg4TransportException>().Which.Kind.Should().Be(Eg4TransportFailureKind.MalformedFrame);
        var seventeen = "00001 22533 01 00 00 057 057 047 053 02 00 000 0035 0552 0000 00.00 11";
        Eg46500ExPi30Protocol.DecodeExtendedStatus(FrameResponse("(" + seventeen)).Fields.Should().HaveCount(17);
    }

    [TestMethod]
    public void PublicDischargingFixture_DecodesScalingDirectionAndDerivedPower()
    {
        var fixture = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "6500ex", "discharging-2022-public-capture.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        fixture.Model.Should().Be("EG4 6500EX-48");
        fixture.Interface.Should().Be("RS232/COM");
        fixture.CaptureKind.Should().Be("reconstructed-from-public-payload");
        var frame = FrameResponse(fixture.ResponsePayload);

        var status = Eg46500ExPi30Protocol.DecodeGeneralStatus(frame);

        status.VoltageV.Should().Be(52.5);
        status.ChargingCurrentA.Should().Be(0);
        status.DischargingCurrentA.Should().Be(16);
        status.ReportedStateOfChargePercent.Should().Be(83);
        (status.DischargingCurrentA - status.ChargingCurrentA).Should().Be(16);
        (status.VoltageV * (status.DischargingCurrentA - status.ChargingCurrentA)).Should().Be(840);
    }

    [TestMethod]
    public void LiveChargingFixture_DecodesCapturedCrcScalingAndCanonicalDirection()
    {
        var fixture = JsonSerializer.Deserialize<LiveFixture>(File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "Fixtures", "6500ex", "charging-2026-08-09-live-hid.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var frame = Convert.FromHexString(fixture.ResponseHex.Replace(" ", string.Empty, StringComparison.Ordinal));

        var status = Eg46500ExPi30Protocol.DecodeGeneralStatus(frame);
        var currentA = status.DischargingCurrentA - status.ChargingCurrentA;

        fixture.CaptureKind.Should().Be("live-hardware-capture");
        fixture.UsbIdentity.Should().Be("0665:5161");
        Eg46500ExPi30Protocol.Encode(Eg46500ExInquiry.GeneralStatus).Should().Equal(
            Convert.FromHexString(fixture.RequestHex.Replace(" ", string.Empty, StringComparison.Ordinal)));
        status.VoltageV.Should().Be(54.4);
        status.ChargingCurrentA.Should().Be(68);
        status.DischargingCurrentA.Should().Be(0);
        status.ReportedStateOfChargePercent.Should().Be(100);
        currentA.Should().Be(-68);
        (status.VoltageV * currentA).Should().BeApproximately(-3699.2, 0.001);
    }

    [TestMethod]
    public void Decode_RejectsCrcFramingNakAndWrongFieldCount()
    {
        var valid = FrameResponse("(PI30");
        valid[^2] ^= 1;
        Action crc = () => Eg46500ExPi30Protocol.DecodePayload(valid);
        Action framing = () => Eg46500ExPi30Protocol.DecodePayload([0x28, 0x00]);
        Action nak = () => Eg46500ExPi30Protocol.DecodePayload(FrameResponse("(NAK"));
        Action fields = () => Eg46500ExPi30Protocol.DecodeGeneralStatus(FrameResponse("(1 2"));

        crc.Should().Throw<Eg4TransportException>().Which.Kind.Should().Be(Eg4TransportFailureKind.Crc);
        framing.Should().Throw<Eg4TransportException>().Which.Kind.Should().Be(Eg4TransportFailureKind.MalformedFrame);
        nak.Should().Throw<Eg4TransportException>().Which.Kind.Should().Be(Eg4TransportFailureKind.Protocol);
        fields.Should().Throw<Eg4TransportException>().Which.Kind.Should().Be(Eg4TransportFailureKind.MalformedFrame);
    }

    [TestMethod]
    public void GeneralStatus_HandlesChargingAndRejectsInvalidSoc()
    {
        var charging = FrameResponse("(000.0 00.0 120.0 60.0 0000 0000 000 360 53.20 012 075 0030 00.0 000.0 00.00 00000 00010000 00 00 00000 010");
        var invalidSoc = FrameResponse("(000.0 00.0 120.0 60.0 0000 0000 000 360 53.20 000 999 0030 00.0 000.0 00.00 00000 00010000 00 00 00000 010");
        var invalidWidth = FrameResponse("(000.0 00.0 120.0 60.0 0000 0000 000 360 153.20 000 075 0030 00.0 000.0 00.00 00000 00010000 00 00 00000 010");

        var status = Eg46500ExPi30Protocol.DecodeGeneralStatus(charging);

        (status.DischargingCurrentA - status.ChargingCurrentA).Should().Be(-12);
        FluentActions.Invoking(() => Eg46500ExPi30Protocol.DecodeGeneralStatus(invalidSoc))
            .Should().Throw<Eg4TransportException>().Which.Kind.Should().Be(Eg4TransportFailureKind.MalformedFrame);
        FluentActions.Invoking(() => Eg46500ExPi30Protocol.DecodeGeneralStatus(invalidWidth))
            .Should().Throw<Eg4TransportException>().Which.Kind.Should().Be(Eg4TransportFailureKind.MalformedFrame);
    }

    [TestMethod]
    public void GeneralStatus_ZeroCurrentMagnitudesProduceIdle()
    {
        var idle = FrameResponse("(000.0 00.0 120.0 60.0 0000 0000 000 360 54.40 000 100 0030 00.0 000.0 00.00 00000 00010000 00 00 00000 010");

        var status = Eg46500ExPi30Protocol.DecodeGeneralStatus(idle);

        (status.DischargingCurrentA - status.ChargingCurrentA).Should().Be(0);
        (status.VoltageV * (status.DischargingCurrentA - status.ChargingCurrentA)).Should().Be(0);
    }

    private static byte[] FrameResponse(string payload)
    {
        var bytes = Encoding.ASCII.GetBytes(payload);
        ushort crc = 0;
        foreach (var value in bytes)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        static byte Adjust(byte value) => value is 0x28 or 0x0D or 0x0A or 0x00 ? (byte)(value + 1) : value;
        return [.. bytes, Adjust((byte)(crc >> 8)), Adjust((byte)crc), 0x0D];
    }

    private sealed record Fixture(string CaptureKind, string Model, string Interface, string ResponsePayload);
    private sealed record LiveFixture(string CaptureKind, string UsbIdentity, string RequestHex, string ResponseHex);
}
