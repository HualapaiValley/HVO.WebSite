using FluentAssertions;
using HVO.Hardware.JkBms.Protocol;

namespace HVO.Hardware.JkBms.Tests.Protocol;

[TestClass]
public class CrcByteSumTests
{
    // ── Compute ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Compute_EmptySpan_ReturnsZero()
    {
        byte crc = CrcByteSum.Compute(ReadOnlySpan<byte>.Empty);
        crc.Should().Be(0x00);
    }

    [TestMethod]
    public void Compute_SingleByte_ReturnsItself()
    {
        byte crc = CrcByteSum.Compute([0x42]);
        crc.Should().Be(0x42);
    }

    [TestMethod]
    public void Compute_Overflow_WrapsModulo256()
    {
        // 0xFF + 0x01 = 0x100 → low byte = 0x00
        byte crc = CrcByteSum.Compute([0xFF, 0x01]);
        crc.Should().Be(0x00);
    }

    [TestMethod]
    public void Compute_KnownBytes_CellInfoCommandCrc()
    {
        // 0xAA + 0x55 + 0x90 + 0xEB + 0x96 = 0x310 → low byte = 0x10
        byte crc = CrcByteSum.Compute([0xAA, 0x55, 0x90, 0xEB, 0x96]);
        crc.Should().Be(0x10);
    }

    // ── AppendCrc ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void AppendCrc_OutputLengthIsInputPlusOne()
    {
        byte[] data = [0x01, 0x02, 0x03];
        byte[] result = CrcByteSum.AppendCrc(data);
        result.Length.Should().Be(data.Length + 1);
    }

    [TestMethod]
    public void AppendCrc_OutputBeginsWithOriginalData()
    {
        byte[] data = [0x11, 0x22, 0x33];
        byte[] result = CrcByteSum.AppendCrc(data);
        result[..3].Should().Equal(data);
    }

    // ── IsValid ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void IsValid_AfterAppendCrc_ReturnsTrue()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05];
        byte[] withCrc = CrcByteSum.AppendCrc(data);
        CrcByteSum.IsValid(withCrc).Should().BeTrue();
    }

    [TestMethod]
    public void IsValid_CorruptedByte_ReturnsFalse()
    {
        byte[] data = [0xAA, 0xBB, 0xCC, 0xDD];
        byte[] withCrc = CrcByteSum.AppendCrc(data);
        withCrc[1] ^= 0xFF; // flip bits in the data
        CrcByteSum.IsValid(withCrc).Should().BeFalse();
    }
}
