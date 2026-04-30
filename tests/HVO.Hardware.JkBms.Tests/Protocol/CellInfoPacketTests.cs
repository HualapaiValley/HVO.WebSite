using FluentAssertions;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Tests.Fakes;

namespace HVO.Hardware.JkBms.Tests.Protocol;

[TestClass]
public class CellInfoPacketTests
{
    // ── Happy-path parsing ─────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_15CellFrame_CellCountIs15()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(cellCount: 15);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.CellCount.Should().Be(15);
    }

    [TestMethod]
    public void Parse_20CellFrame_CellCountIs20()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(cellCount: 20);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.CellCount.Should().Be(20);
    }

    [TestMethod]
    public void Parse_CellVoltages_MatchBuilderInput()
    {
        ushort[] voltages = [3300, 3310, 3290, 3305, 3298, 3312, 3301, 3297,
                             3308, 3303, 3295, 3315, 3299, 3302, 3307];
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(cellCount: 15, cellVoltagesMv: voltages);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);

        packet.CellVoltagesMv.Should().Equal(voltages);
    }

    [TestMethod]
    public void Parse_CellVoltagesCount_EqualsCellCount()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(cellCount: 15);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.CellVoltagesMv.Count.Should().Be(packet.CellCount);
    }

    [TestMethod]
    public void Parse_AverageCellVoltage_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(averageCellVoltageMv: 3310);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.AverageCellVoltageMv.Should().Be(3310);
    }

    [TestMethod]
    public void Parse_DeltaCellVoltage_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(deltaCellVoltageMv: 12);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.DeltaCellVoltageMv.Should().Be(12);
    }

    [TestMethod]
    public void Parse_MaxMinCellIndex_MatchBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(maxCellIndex: 3, minCellIndex: 11);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.MaxVoltageCellIndex.Should().Be(3);
        packet.MinVoltageCellIndex.Should().Be(11);
    }

    [TestMethod]
    public void Parse_BalancingActive_TrueWhenBuilderSetsOne()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(balancingActive: 1);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.BalancingActive.Should().BeTrue();
    }

    [TestMethod]
    public void Parse_BalancingCurrent_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(balancingCurrentMa: 25);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.BalancingCurrentMa.Should().BeApproximately(25.0, 0.01);
    }

    // ── Temperature decoding ──────────────────────────────────────────────────

    [TestMethod]
    public void DecodeTemperature_0_Returns0C()
    {
        // raw = 0 → 0 × 0.1 = 0 °C
        CellInfoPacket.DecodeTemperature(0).Should().BeApproximately(0.0, 0.001);
    }

    [TestMethod]
    public void DecodeTemperature_250_Returns25C()
    {
        // raw = 250 → 250 × 0.1 = 25.0 °C
        CellInfoPacket.DecodeTemperature(250).Should().BeApproximately(25.0, 0.001);
    }

    [TestMethod]
    public void DecodeTemperature_Minus100_ReturnsMinus10C()
    {
        // raw = -100 → -100 × 0.1 = -10.0 °C
        CellInfoPacket.DecodeTemperature(-100).Should().BeApproximately(-10.0, 0.001);
    }

    [TestMethod]
    public void Parse_Temperatures_MatchBuilderRaw()
    {
        // powerTubeRaw = 250 → 25.0 °C; battTemp1Raw = 300 → 30.0 °C; battTemp2Raw = 150 → 15.0 °C
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(
            powerTubeRaw: 250,
            battTemp1Raw: 300,
            battTemp2Raw: 150);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);

        packet.PowerTubeTemperatureC.Should().BeApproximately(25.0, 0.001);
        packet.BatteryTemperature1C.Should().BeApproximately(30.0, 0.001);
        packet.BatteryTemperature2C.Should().BeApproximately(15.0, 0.001);
    }

    // ── Pack electrical ───────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_TotalVoltageMv_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(totalVoltageMv: 49_500);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.TotalVoltageMv.Should().Be(49_500);
    }

    [TestMethod]
    public void Parse_CurrentMa_PositiveDischarge()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(currentMa: 10_000);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.CurrentMa.Should().Be(10_000);
    }

    [TestMethod]
    public void Parse_CurrentMa_NegativeCharging()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(currentMa: -5_000);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.CurrentMa.Should().Be(-5_000);
    }

    // ── Capacity and state ────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_StateOfCharge_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(socPercent: 75);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.StateOfChargePercent.Should().Be(75);
    }

    [TestMethod]
    public void Parse_NominalCapacity_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(nominalMah: 200_000);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.NominalCapacityMah.Should().Be(200_000);
    }

    [TestMethod]
    public void Parse_CycleCount_MatchesBuilderInput()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(cycleCount: 42);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.CycleCount.Should().Be(42);
    }

    // ── Alarms ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_ZeroAlarmBitmask_HasAlarmsIsFalse()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(alarmBitmask: 0);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.HasAlarms.Should().BeFalse();
    }

    [TestMethod]
    public void Parse_NonZeroAlarmBitmask_HasAlarmsIsTrue()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(alarmBitmask: 0x01);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.HasAlarms.Should().BeTrue();
        packet.AlarmBitmask.Should().Be(0x01u);
    }

    // ── Error conditions ──────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_DataTooShort_ThrowsJkBmsFrameException()
    {
        byte[] tooShort = new byte[0x10];
        var act = () => CellInfoPacket.Parse(tooShort);
        act.Should().Throw<JkBmsFrameException>();
    }

    [TestMethod]
    public void Parse_ZeroCellCount_ThrowsJkBmsFrameException()
    {
        // Build a valid frame but zero out the enabled-cells bitmask (all 4 bytes)
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(cellCount: 15);
        // Bitmask is at header offset 6 + data offset 0x30; zero all 4 bytes
        frame[6 + 0x30] = 0x00;
        frame[6 + 0x31] = 0x00;
        frame[6 + 0x32] = 0x00;
        frame[6 + 0x33] = 0x00;
        byte[] data = JkBmsProtocol.GetData(frame).ToArray();
        var act = () => CellInfoPacket.Parse(data);
        act.Should().Throw<JkBmsFrameException>();
    }
}
