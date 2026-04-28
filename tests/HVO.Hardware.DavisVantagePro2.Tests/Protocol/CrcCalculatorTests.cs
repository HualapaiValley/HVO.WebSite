using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Protocol;

namespace HVO.Hardware.DavisVantagePro2.Tests.Protocol;

[TestClass]
public class CrcCalculatorTests
{
    // ── Compute ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Compute_EmptySpan_ReturnsZero()
    {
        ushort crc = CrcCalculator.Compute(ReadOnlySpan<byte>.Empty);
        crc.Should().Be(0x0000);
    }

    [TestMethod]
    public void Compute_KnownVector_SingleByteA_Returns0x58E5()
    {
        // Pre-computed CRC-CCITT-16 of {0x41} ('A') = 0x58E5
        ushort crc = CrcCalculator.Compute([0x41]);
        crc.Should().Be(0x58E5);
    }

    [TestMethod]
    public void Compute_SameBytes_GivesSameResult()
    {
        byte[] a = [0xDE, 0xAD, 0xBE, 0xEF];
        byte[] b = [0xDE, 0xAD, 0xBE, 0xEF];
        CrcCalculator.Compute(a).Should().Be(CrcCalculator.Compute(b));
    }

    [TestMethod]
    public void Compute_DifferentBytes_GivesDifferentResult()
    {
        byte[] a = [0xDE, 0xAD, 0xBE, 0xEF];
        byte[] b = [0xDE, 0xAD, 0xBE, 0xFF]; // last byte changed
        CrcCalculator.Compute(a).Should().NotBe(CrcCalculator.Compute(b));
    }

    // ── AppendCrc ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void AppendCrc_OutputIsInputPlusTwoBytes()
    {
        byte[] data = [0x01, 0x02, 0x03];
        byte[] result = CrcCalculator.AppendCrc(data);
        result.Length.Should().Be(data.Length + 2);
    }

    [TestMethod]
    public void AppendCrc_OutputBeginsWithOriginalData()
    {
        byte[] data = [0x11, 0x22, 0x33];
        byte[] result = CrcCalculator.AppendCrc(data);
        result[..3].Should().Equal(data);
    }

    [TestMethod]
    public void AppendCrc_EmptyData_ProducesTwoByteOutput()
    {
        byte[] result = CrcCalculator.AppendCrc([]);
        result.Length.Should().Be(2);
    }

    // ── IsValid ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void IsValid_AfterAppendCrc_ReturnsTrue()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05];
        byte[] withCrc = CrcCalculator.AppendCrc(data);
        CrcCalculator.IsValid(withCrc).Should().BeTrue();
    }

    [TestMethod]
    public void IsValid_EmptyDataWithAppendedCrc_ReturnsTrue()
    {
        byte[] withCrc = CrcCalculator.AppendCrc([]);
        CrcCalculator.IsValid(withCrc).Should().BeTrue();
    }

    [TestMethod]
    public void IsValid_CorruptedDataByte_ReturnsFalse()
    {
        byte[] data = [0x10, 0x20, 0x30];
        byte[] withCrc = CrcCalculator.AppendCrc(data);

        withCrc[1] ^= 0xFF; // flip bits in the middle byte

        CrcCalculator.IsValid(withCrc).Should().BeFalse();
    }

    [TestMethod]
    public void IsValid_CorruptedCrcByte_ReturnsFalse()
    {
        byte[] data = [0xAA, 0xBB, 0xCC];
        byte[] withCrc = CrcCalculator.AppendCrc(data);

        withCrc[^1] ^= 0x01; // flip one bit of the last CRC byte

        CrcCalculator.IsValid(withCrc).Should().BeFalse();
    }

    [TestMethod]
    public void IsValid_NonZeroDataWithZeroAppendedCrc_ReturnsFalse()
    {
        // Two bytes of data followed by two zero bytes — the appended bytes are
        // NOT the correct CRC of the data, so validation must fail.
        byte[] buf = [0xDE, 0xAD, 0x00, 0x00];
        CrcCalculator.IsValid(buf).Should().BeFalse();
    }

    // ── Round-trip property ───────────────────────────────────────────────────

    [TestMethod]
    public void AppendCrcThenCompute_ReturnsZero()
    {
        // The CRC-CCITT-16 property: Compute(data || CRC(data)) == 0
        byte[] data = [0xCA, 0xFE, 0xBA, 0xBE];
        byte[] withCrc = CrcCalculator.AppendCrc(data);
        CrcCalculator.Compute(withCrc).Should().Be(0x0000);
    }
}
