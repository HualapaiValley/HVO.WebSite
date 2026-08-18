using FluentAssertions;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Tests.Fakes;
using ProductionCrc = HVO.Hardware.JkBms.Protocol.CrcByteSum;

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
    public void Parse_CellResistances_MatchBuilderInput()
    {
        ushort[] resistances = [12, 11, 13, 10, 12, 14, 11, 13, 12, 10, 11, 12, 13, 14, 12];
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(
            cellCount: 15,
            cellResistancesMOhm: resistances);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);

        packet.CellResistancesMOhm.Should().Equal(resistances);
    }

    [TestMethod]
    public void Parse_CellResistancesCount_EqualsCellCount()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(cellCount: 15);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.CellResistancesMOhm.Count.Should().Be(packet.CellCount);
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
    public void Parse_CurrentMa_PositiveCharge()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(currentMa: 10_000);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.CurrentMa.Should().Be(10_000);
    }

    [TestMethod]
    public void Parse_CurrentMa_NegativeDischarge()
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
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(alarmBitmask: 0x1234);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.HasAlarms.Should().BeTrue();
        packet.AlarmBitmask.Should().Be(0x1234u);
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
    public void Parse_ZeroBitmask_FallsBackToVoltageCount()
    {
        // Newer JK BMS hardware (MAC prefix C8:47:8C:EC / EA) leaves the enabled-cells
        // bitmask at 0x30 as 0x00000000. The parser must fall back to counting consecutive
        // non-zero voltage entries and still produce a valid parse.
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(cellCount: 15);
        // Zero out the bitmask
        frame[6 + 0x30] = 0x00;
        frame[6 + 0x31] = 0x00;
        frame[6 + 0x32] = 0x00;
        frame[6 + 0x33] = 0x00;
        // Recalculate CRC after mutating the frame
        frame[299] = ProductionCrc.Compute(frame.AsSpan(0, 299));
        byte[] data = JkBmsProtocol.GetData(frame).ToArray();
        var packet = CellInfoPacket.Parse(data);
        packet.CellCount.Should().Be(15);
    }

    [TestMethod]
    public void Parse_ZeroBitmaskAndZeroVoltages_ThrowsJkBmsFrameException()
    {
        // When both the bitmask and all cell voltages are zero, no cell count can be
        // determined — this should still throw a JkBmsFrameException.
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame(cellCount: 15);
        // Zero bitmask
        frame[6 + 0x30] = 0x00;
        frame[6 + 0x31] = 0x00;
        frame[6 + 0x32] = 0x00;
        frame[6 + 0x33] = 0x00;
        // Zero all 24 cell voltage slots (bytes 0–47 in data section)
        for (int i = 0; i < 48; i++)
            frame[6 + i] = 0x00;
        // Recalculate CRC after mutating the frame
        frame[299] = ProductionCrc.Compute(frame.AsSpan(0, 299));
        byte[] data = JkBmsProtocol.GetData(frame).ToArray();
        var act = () => CellInfoPacket.Parse(data);
        act.Should().Throw<JkBmsFrameException>();
    }

    // ── JK02_32S variant ──────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_32S_CellCount_DerivedFromVoltageEntries()
    {
        // 32S frames have enabledMask == 0; cell count is derived from consecutive
        // non-zero voltage entries in the data section.
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame32S(cellCount: 16);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.CellCount.Should().Be(16);
    }

    [TestMethod]
    public void Parse_32S_TotalVoltage_ReadFromOffset0x90()
    {
        // In the 32S layout the pack voltage is at data offset 0x90 (not 0x70).
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame32S(totalVoltageMv: 52_800);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.TotalVoltageMv.Should().Be(52_800);
    }

    [TestMethod]
    public void Parse_32S_AverageCellVoltage_ReadFromOffset0x44()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame32S(averageCellVoltageMv: 3305);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.AverageCellVoltageMv.Should().Be(3305);
    }

    [TestMethod]
    public void Parse_32S_CellResistances_ReadFromOffset0x4A()
    {
        // In the 32S layout resistances start at 0x4A (not 0x3A as in 24S).
        ushort[] resistances = Enumerable.Range(1, 16).Select(i => (ushort)(10 + i)).ToArray();
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame32S(
            cellCount: 16,
            cellResistancesMOhm: resistances);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.CellResistancesMOhm.Should().Equal(resistances);
    }

    [TestMethod]
    public void Parse_32S_AlarmBitmask_ReadsAllFourLittleEndianBytesFromOffset0xA0()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame32S(alarmBitmask: 0x0800_0040);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.AlarmBitmask.Should().Be(0x0800_0040u);
        packet.HasAlarms.Should().BeTrue();
    }

    [TestMethod]
    public void Parse_32S_Temperatures_ReadFromCorrectOffsets()
    {
        // PowerTubeTemp at 0x8A, BatteryTemp1 at 0x9C, BatteryTemp2 at 0x9E.
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame32S(
            powerTubeRaw: 350,   // 35.0°C
            battTemp1Raw: 280,   // 28.0°C
            battTemp2Raw: 260);  // 26.0°C
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.PowerTubeTemperatureC.Should().BeApproximately(35.0, 0.001);
        packet.BatteryTemperature1C.Should().BeApproximately(28.0, 0.001);
        packet.BatteryTemperature2C.Should().BeApproximately(26.0, 0.001);
    }

    [TestMethod]
    public void Parse_32S_StateOfCharge_ReadFromOffset0xA7()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame32S(socPercent: 73);
        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);
        packet.StateOfChargePercent.Should().Be(73);
    }
}
