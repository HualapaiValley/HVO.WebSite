using FluentAssertions;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Tests.Fakes;

namespace HVO.Hardware.JkBms.Tests.Protocol;

[TestClass]
public class DeviceInfoPacketTests
{
    [TestMethod]
    public void Parse_ManufacturerName_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildDeviceInfoFrame(manufacturer: "JIKONG");
        var data = JkBmsProtocol.GetData(frame);
        var packet = DeviceInfoPacket.Parse(data);
        packet.ManufacturerName.Should().Be("JIKONG");
    }

    [TestMethod]
    public void Parse_HardwareName_MatchesBuilderInput()
    {
        // HardwareName field is 8 bytes; use a name that fits exactly
        byte[] frame = TestFrameBuilder.BuildDeviceInfoFrame(hardwareName: "JK-B2A24");
        var data = JkBmsProtocol.GetData(frame);
        var packet = DeviceInfoPacket.Parse(data);
        packet.HardwareName.Should().Be("JK-B2A24");
    }

    [TestMethod]
    public void Parse_FirmwareVersion_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildDeviceInfoFrame(firmwareVersion: "V10.2");
        var data = JkBmsProtocol.GetData(frame);
        var packet = DeviceInfoPacket.Parse(data);
        packet.FirmwareVersion.Should().Be("V10.2");
    }

    [TestMethod]
    public void Parse_DataTooShort_ThrowsJkBmsFrameException()
    {
        byte[] tooShort = new byte[5];
        var act = () => DeviceInfoPacket.Parse(tooShort);
        act.Should().Throw<JkBmsFrameException>();
    }

    [TestMethod]
    public void Parse_NullPaddedStrings_AreTrimmed()
    {
        byte[] frame = TestFrameBuilder.BuildDeviceInfoFrame(manufacturer: "AB");
        var data = JkBmsProtocol.GetData(frame);
        var packet = DeviceInfoPacket.Parse(data);
        // Should be "AB" not "AB\0\0\0\0\0\0\0\0"
        packet.ManufacturerName.Should().NotContain("\0");
        packet.ManufacturerName.Should().Be("AB");
    }
}
