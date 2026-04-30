using FluentAssertions;
using HVO.Hardware.JkBms.Components;

namespace HVO.Hardware.JkBms.Tests.Components;

[TestClass]
public class AlarmDecoderTests
{
    // ── Zero bitmask ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Decode_ZeroBitmask_ReturnsEmpty()
    {
        AlarmDecoder.Decode(0u).Should().BeEmpty();
    }

    // ── Individual known bits ─────────────────────────────────────────────────

    [TestMethod]
    [DataRow(0,  "Charge Overtemperature")]
    [DataRow(1,  "Charge Undertemperature")]
    [DataRow(2,  "Cell Overvoltage (cell)")]
    [DataRow(3,  "Cell Undervoltage")]
    [DataRow(4,  "Pack Undervoltage")]
    [DataRow(5,  "Discharge Overcurrent")]
    [DataRow(6,  "Charge Overcurrent")]
    [DataRow(7,  "Discharge Overtemperature")]
    [DataRow(8,  "Short Circuit")]
    [DataRow(9,  "Discharge Undertemperature")]
    [DataRow(10, "Charge Overcurrent (protection)")]
    [DataRow(11, "Cell Overvoltage (protection)")]
    [DataRow(12, "Cell Overvoltage")]
    [DataRow(13, "Pack Overvoltage")]
    [DataRow(14, "Low Capacity")]
    [DataRow(15, "MOS Overtemperature")]
    public void Decode_SingleKnownBit_ReturnsExactlyThatAlarmName(int bit, string expectedName)
    {
        uint bitmask = 1u << bit;

        var result = AlarmDecoder.Decode(bitmask).ToList();

        result.Should().ContainSingle()
              .Which.Should().Be(expectedName);
    }

    // ── Unknown bits (16–31) ──────────────────────────────────────────────────

    [TestMethod]
    [DataRow(16)]
    [DataRow(20)]
    [DataRow(31)]
    public void Decode_UnknownBit_ReturnsUnknownFlagWithBitNumber(int bit)
    {
        uint bitmask = 1u << bit;

        var result = AlarmDecoder.Decode(bitmask).ToList();

        result.Should().ContainSingle()
              .Which.Should().Be($"Unknown flag (bit {bit})");
    }

    // ── Multiple simultaneous alarms ──────────────────────────────────────────

    [TestMethod]
    public void Decode_MultipleKnownBits_ReturnsAllNames()
    {
        // Cell Undervoltage (bit 3) + Pack Undervoltage (bit 4) + Low Capacity (bit 14)
        uint bitmask = (1u << 3) | (1u << 4) | (1u << 14);

        var result = AlarmDecoder.Decode(bitmask).ToList();

        result.Should().HaveCount(3)
              .And.ContainInOrder(
                  "Cell Undervoltage",
                  "Pack Undervoltage",
                  "Low Capacity");
    }

    [TestMethod]
    public void Decode_MixedKnownAndUnknownBits_ReturnsAll()
    {
        // Cell Overvoltage (bit 12) + unknown (bit 20)
        uint bitmask = (1u << 12) | (1u << 20);

        var result = AlarmDecoder.Decode(bitmask).ToList();

        result.Should().HaveCount(2)
              .And.ContainInOrder("Cell Overvoltage", "Unknown flag (bit 20)");
    }

    // ── All known bits set ────────────────────────────────────────────────────

    [TestMethod]
    public void Decode_AllKnownBitsSet_Returns16Names()
    {
        uint bitmask = 0x0000_FFFFu; // bits 0–15

        var result = AlarmDecoder.Decode(bitmask).ToList();

        result.Should().HaveCount(16);
    }

    // ── Ordering ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Decode_MultipleAlarms_AreReturnedInAscendingBitOrder()
    {
        // bits 7, 3, 0 — set in reverse order; result must be low→high
        uint bitmask = (1u << 7) | (1u << 3) | (1u << 0);

        var result = AlarmDecoder.Decode(bitmask).ToList();

        result.Should().ContainInOrder(
            "Charge Overtemperature",   // bit 0
            "Cell Undervoltage",         // bit 3
            "Discharge Overtemperature"); // bit 7
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [TestMethod]
    public void Decode_CalledTwiceWithSameBitmask_ReturnsSameResult()
    {
        uint bitmask = (1u << 5) | (1u << 13);

        var first  = AlarmDecoder.Decode(bitmask).ToList();
        var second = AlarmDecoder.Decode(bitmask).ToList();

        first.Should().Equal(second);
    }
}
