using System.Text.Json;
using FluentAssertions;

namespace HVO.Hardware.Eg4.Tests.Protocol;

[TestClass]
public sealed class Eg4Mppt10048HvEvidenceTests
{
    [TestMethod]
    public void StandbyCapture_ProvesCrcResponseShapeAndRepeatabilityButNoTelemetryMap()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "mppt100-48hv", "standby-2026-08-09.json")));
        var root = document.RootElement;
        root.GetProperty("captureKind").GetString().Should().Be("hardware-capture");
        root.GetProperty("model").GetString().Should().Be("EG4 MPPT100-48HV");
        root.GetProperty("state").GetString().Should().Be("standby-no-pv");
        root.TryGetProperty("expected", out _).Should().BeFalse("no controller telemetry meaning is validated");
        var transactions = root.GetProperty("transactions").EnumerateArray().ToArray();
        transactions.Should().HaveCount(3);

        foreach (var transaction in transactions)
        {
            var request = Hex(transaction.GetProperty("requestHex").GetString()!);
            var response = Hex(transaction.GetProperty("responseHex").GetString()!);
            request[0].Should().Be(1);
            request[1].Should().Be(3);
            response[0].Should().Be(1);
            response[1].Should().Be(3);
            HasValidModbusCrc(request).Should().BeTrue();
            HasValidModbusCrc(response).Should().BeTrue();
            response.Length.Should().Be(5 + response[2]);
            response[2].Should().Be((byte)((request[4] * 256 + request[5]) * 2));
        }

        transactions[0].GetProperty("responseHex").GetString()
            .Should().Be(transactions[1].GetProperty("responseHex").GetString());
        var initial = transactions[0].GetProperty("observedAtUtc").GetDateTimeOffset();
        var repeated = transactions[1].GetProperty("observedAtUtc").GetDateTimeOffset();
        (repeated - initial).Should().BeGreaterThan(TimeSpan.FromSeconds(80)).And.BeLessThan(TimeSpan.FromSeconds(90));

        var corrupted = Hex(transactions[0].GetProperty("responseHex").GetString()!);
        corrupted[^1] ^= 1;
        HasValidModbusCrc(corrupted).Should().BeFalse();
    }

    [TestMethod]
    public void OperatorReportedCandidateBatteryPoll_IsValidReadOnlyModbusFrame()
    {
        var request = Hex("01 03 00 13\n00 11\t74 03");

        HasValidModbusCrc(request).Should().BeTrue();
        HasValidModbusCrc([0x01]).Should().BeFalse();
        request[1].Should().Be(3);
        request.AsSpan(2, 4).ToArray().Should().Equal(0x00, 0x13, 0x00, 0x11);
    }

    private static bool HasValidModbusCrc(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 4)
            return false;
        ushort crc = 0xFFFF;
        foreach (var value in frame[..^2])
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
        }
        return frame[^2] == (byte)crc && frame[^1] == (byte)(crc >> 8);
    }

    private static byte[] Hex(string value) => Convert.FromHexString(string.Concat(value.Where(character => !char.IsWhiteSpace(character))));
}
