using FluentAssertions;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Tests.Fakes;

namespace HVO.Hardware.JkBms.Tests.Protocol;

[TestClass]
public class JkBmsProtocolTests
{
    // ── BuildCellInfoCommand ──────────────────────────────────────────────────

    [TestMethod]
    public void BuildCellInfoCommand_Returns20Bytes()
    {
        JkBmsProtocol.BuildCellInfoCommand().Length.Should().Be(20);
    }

    [TestMethod]
    public void BuildCellInfoCommand_StartsWithExpectedPreamble()
    {
        var cmd = JkBmsProtocol.BuildCellInfoCommand();
        cmd[0..4].Should().Equal(0xAA, 0x55, 0x90, 0xEB);
    }

    [TestMethod]
    public void BuildCellInfoCommand_HasCommandByte0x96()
    {
        var cmd = JkBmsProtocol.BuildCellInfoCommand();
        cmd[4].Should().Be(0x96);
    }

    // ── BuildDeviceInfoCommand ────────────────────────────────────────────────

    [TestMethod]
    public void BuildDeviceInfoCommand_Returns20Bytes()
    {
        JkBmsProtocol.BuildDeviceInfoCommand().Length.Should().Be(20);
    }

    [TestMethod]
    public void BuildDeviceInfoCommand_HasCommandByte0x97()
    {
        var cmd = JkBmsProtocol.BuildDeviceInfoCommand();
        cmd[4].Should().Be(0x97);
    }

    // ── TryAccumulateFrame — single chunk ─────────────────────────────────────

    [TestMethod]
    public void TryAccumulateFrame_CompleteFrameInOneChunk_ReturnsTrue()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame();
        var buffer = new List<byte>();

        bool result = JkBmsProtocol.TryAccumulateFrame(buffer, frame, out var assembled);

        result.Should().BeTrue();
        assembled.Should().Equal(frame);
    }

    [TestMethod]
    public void TryAccumulateFrame_CompleteFrameInOneChunk_ClearsBuffer()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame();
        var buffer = new List<byte>();

        JkBmsProtocol.TryAccumulateFrame(buffer, frame, out _);

        buffer.Should().BeEmpty();
    }

    // ── TryAccumulateFrame — chunked ──────────────────────────────────────────

    [TestMethod]
    public void TryAccumulateFrame_MultiChunk_ReturnsTrueOnLastChunk()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame();
        var chunks = TestFrameBuilder.SplitIntoChunks(frame, 20);
        var buffer = new List<byte>();

        bool result = false;
        byte[] assembled = [];
        foreach (var chunk in chunks)
        {
            result = JkBmsProtocol.TryAccumulateFrame(buffer, chunk, out assembled);
            if (result) break;
        }

        result.Should().BeTrue();
        assembled.Should().Equal(frame);
    }

    [TestMethod]
    public void TryAccumulateFrame_IncompleteData_ReturnsFalse()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame();
        var buffer = new List<byte>();

        // Only send the first 10 bytes (header not even complete)
        bool result = JkBmsProtocol.TryAccumulateFrame(buffer, frame[..10], out _);

        result.Should().BeFalse();
    }

    [TestMethod]
    public void TryAccumulateFrame_LeadingGarbage_DiscardedAndFrameReturned()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame();
        byte[] garbage = [0x00, 0xFF, 0x12, 0x34];
        byte[] input = [.. garbage, .. frame];
        var buffer = new List<byte>();

        bool result = JkBmsProtocol.TryAccumulateFrame(buffer, input, out var assembled);

        result.Should().BeTrue();
        assembled.Should().Equal(frame);
    }

    [TestMethod]
    public void TryAccumulateFrame_TwoFrames_ReturnsFirstAndLeavesSecondInBuffer()
    {
        byte[] frame1 = TestFrameBuilder.BuildCellInfoFrame(cellCount: 15);
        byte[] frame2 = TestFrameBuilder.BuildDeviceInfoFrame();
        var buffer = new List<byte>();

        // Feed both frames at once
        bool result1 = JkBmsProtocol.TryAccumulateFrame(buffer, [.. frame1, .. frame2], out var assembled1);

        result1.Should().BeTrue();
        assembled1.Should().Equal(frame1);
        buffer.Should().Equal(frame2); // second frame remains
    }

    // ── ValidateCrc ───────────────────────────────────────────────────────────

    [TestMethod]
    public void ValidateCrc_ValidFrame_ReturnsTrue()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame();
        JkBmsProtocol.ValidateCrc(frame).Should().BeTrue();
    }

    [TestMethod]
    public void ValidateCrc_CorruptedData_ReturnsFalse()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame();
        frame[10] ^= 0xFF;
        JkBmsProtocol.ValidateCrc(frame).Should().BeFalse();
    }

    [TestMethod]
    public void ValidateCrc_WrongSof_ReturnsFalse()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame();
        frame[0] = 0x00; // corrupt SOF
        JkBmsProtocol.ValidateCrc(frame).Should().BeFalse();
    }

    [TestMethod]
    public void ValidateCrc_TooShort_ReturnsFalse()
    {
        JkBmsProtocol.ValidateCrc([0x55, 0xAA, 0xEB, 0x90]).Should().BeFalse();
    }

    // ── GetFrameType ──────────────────────────────────────────────────────────

    [TestMethod]
    public void GetFrameType_CellInfoFrame_ReturnsCellInfoType()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame();
        JkBmsProtocol.GetFrameType(frame).Should().Be(JkBmsProtocol.FrameTypeCellInfo);
    }

    [TestMethod]
    public void GetFrameType_DeviceInfoFrame_ReturnsDeviceInfoType()
    {
        byte[] frame = TestFrameBuilder.BuildDeviceInfoFrame();
        JkBmsProtocol.GetFrameType(frame).Should().Be(JkBmsProtocol.FrameTypeDeviceInfo);
    }

    // ── GetData ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetData_CellInfoFrame_ReturnsDataSectionLength()
    {
        byte[] frame = TestFrameBuilder.BuildCellInfoFrame();
        var data = JkBmsProtocol.GetData(frame);
        data.Length.Should().Be(293); // 300-byte frame minus 6-byte header minus 1-byte CRC
    }
}
