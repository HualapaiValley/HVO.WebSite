using FluentAssertions;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Tests.Fakes;

namespace HVO.Hardware.JkBms.Tests.Protocol;

[TestClass]
public class SettingsPacketTests
{
    // ── Happy-path parsing ─────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_CellUndervoltageProtection_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(cellUvpMv: 2950);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.CellUndervoltageProtectionMv.Should().Be(2950);
    }

    [TestMethod]
    public void Parse_CellUndervoltageRecovery_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(cellUvprMv: 3050);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.CellUndervoltageRecoveryMv.Should().Be(3050);
    }

    [TestMethod]
    public void Parse_CellOvervoltageProtection_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(cellOvpMv: 4250);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.CellOvervoltageProtectionMv.Should().Be(4250);
    }

    [TestMethod]
    public void Parse_CellOvervoltageRecovery_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(cellOvprMv: 4150);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.CellOvervoltageRecoveryMv.Should().Be(4150);
    }

    [TestMethod]
    public void Parse_BalancePressureDifference_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(balanceDeltaMv: 15);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.BalancePressureDifferenceMv.Should().Be(15);
    }

    [TestMethod]
    public void Parse_BalanceStartingVoltage_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(balanceStartMv: 3350);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.BalanceStartingVoltageMv.Should().Be(3350);
    }

    [TestMethod]
    public void Parse_BalancingEnabled_True_WhenBuilderSetsTrue()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(balancingEnabled: true);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.BalancingEnabled.Should().BeTrue();
    }

    [TestMethod]
    public void Parse_BalancingEnabled_False_WhenBuilderSetsFalse()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(balancingEnabled: false);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.BalancingEnabled.Should().BeFalse();
    }

    [TestMethod]
    public void Parse_ChargingOcp_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(chargeOcpMa: 60_000);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.ChargingOvercurrentProtectionMa.Should().Be(60_000);
    }

    [TestMethod]
    public void Parse_ChargingOcpDelay_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(chargeOcpDelayS: 5);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.ChargingOvercurrentProtectionDelayS.Should().Be(5);
    }

    [TestMethod]
    public void Parse_ChargingOcpRecovery_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(chargeOcpRecoveryS: 60);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.ChargingOvercurrentProtectionRecoveryS.Should().Be(60);
    }

    [TestMethod]
    public void Parse_DischargingOcp_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(dischargeOcpMa: 120_000);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.DischargingOvercurrentProtectionMa.Should().Be(120_000);
    }

    [TestMethod]
    public void Parse_DischargingOcpDelay_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(dischargeOcpDelayS: 3);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.DischargingOvercurrentProtectionDelayS.Should().Be(3);
    }

    [TestMethod]
    public void Parse_DischargingOcpRecovery_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(dischargeOcpRecoveryS: 45);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.DischargingOvercurrentProtectionRecoveryS.Should().Be(45);
    }

    [TestMethod]
    public void Parse_ShortCircuitRecovery_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(scpRecoveryS: 60);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.ShortCircuitProtectionRecoveryS.Should().Be(60);
    }

    [TestMethod]
    public void Parse_ShortCircuitDelay_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(scpDelayUs: 200);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.ShortCircuitProtectionDelayUs.Should().Be(200);
    }

    [TestMethod]
    public void Parse_ChargeOvertemperatureProtection_MatchesBuilderInput()
    {
        // Raw 450 = 45.0°C
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(chargeOtpRaw: 450);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.ChargingOvertemperatureProtectionC.Should().BeApproximately(45.0, 0.01);
    }

    [TestMethod]
    public void Parse_ChargeUndertemperatureProtection_NegativeValue()
    {
        // Raw -100 = -10.0°C
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(chargeUtpRaw: -100);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.ChargingUndertemperatureProtectionC.Should().BeApproximately(-10.0, 0.01);
    }

    [TestMethod]
    public void Parse_DischargeOvertemperatureProtection_MatchesBuilderInput()
    {
        // Raw 600 = 60.0°C
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(dischargeOtpRaw: 600);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.DischargingOvertemperatureProtectionC.Should().BeApproximately(60.0, 0.01);
    }

    [TestMethod]
    public void Parse_PowerTubeOvertemperatureProtection_MatchesBuilderInput()
    {
        // Raw 750 = 75.0°C
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(mosOtpRaw: 750);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.PowerTubeOvertemperatureProtectionC.Should().BeApproximately(75.0, 0.01);
    }

    [TestMethod]
    public void Parse_CellCount_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(cellCount: 24);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.CellCount.Should().Be(24);
    }

    [TestMethod]
    public void Parse_ChargingEnabled_True_WhenBuilderSetsTrue()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(chargingEnabled: true);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.ChargingEnabled.Should().BeTrue();
    }

    [TestMethod]
    public void Parse_ChargingEnabled_False_WhenBuilderSetsFalse()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(chargingEnabled: false);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.ChargingEnabled.Should().BeFalse();
    }

    [TestMethod]
    public void Parse_DischargingEnabled_True_WhenBuilderSetsTrue()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(dischargingEnabled: true);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.DischargingEnabled.Should().BeTrue();
    }

    [TestMethod]
    public void Parse_NominalCapacity_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame(nominalCapacityMah: 200_000);
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        packet.NominalCapacityMah.Should().Be(200_000);
    }

    // ── Error cases ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_TooShortData_ThrowsJkBmsFrameException()
    {
        byte[] tooShort = new byte[0x50]; // Less than required 0x88
        var act = () => SettingsPacket.Parse(tooShort);
        act.Should().Throw<JkBmsFrameException>();
    }

    [TestMethod]
    public void Parse_ExactMinimumLength_Succeeds()
    {
        byte[] frame = TestFrameBuilder.BuildSettingsFrame();
        var data = JkBmsProtocol.GetData(frame);
        // The full data section (293 bytes) is well above minimum — trim to exactly 0x88
        byte[] exact = data[..0x88].ToArray();
        var act = () => SettingsPacket.Parse(exact);
        act.Should().NotThrow();
    }

    // ── Timestamp ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_RecordedAtUtc_IsRecent()
    {
        var before = DateTime.UtcNow;
        byte[] frame = TestFrameBuilder.BuildSettingsFrame();
        var data = JkBmsProtocol.GetData(frame);
        var packet = SettingsPacket.Parse(data);
        var after = DateTime.UtcNow;

        packet.RecordedAtUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }
}
