using FluentAssertions;
using HVO.Hardware.VictronSmartShunt.SmartShunt;

namespace HVO.Hardware.VictronSmartShunt.Tests.SmartShunt;

[TestClass]
public sealed class SmartShuntPublicProtocolTests
{
    [TestMethod]
    public void DecodeSample_MapsKnownPublicFields()
    {
        var recordedAt = DateTime.Parse("2026-05-24T20:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var sample = SmartShuntPublicProtocol.DecodeSample(new Dictionary<string, byte[]>
        {
            ["soc"] = Convert.FromHexString("dc23"),
            ["voltage"] = Convert.FromHexString("0015"),
            ["current"] = Convert.FromHexString("9d560000"),
            ["power"] = Convert.FromHexString("db04"),
            ["consumed_ah"] = Convert.FromHexString("70f9ffff"),
            ["starter_voltage"] = Convert.FromHexString("d204"),
            ["temperature"] = Convert.FromHexString("1a00"),
            ["remaining_time"] = Convert.FromHexString("ffff"),
        }, recordedAt);

        sample.RecordedAtUtc.Should().Be(recordedAt);
        sample.StateOfChargePercent.Should().Be(91.80);
        sample.VoltageV.Should().Be(53.76);
        sample.CurrentA.Should().Be(22.173);
        sample.PowerW.Should().Be(1243);
        sample.ConsumedAh.Should().Be(-168.0);
        sample.StarterVoltageV.Should().Be(12.34);
        sample.TemperatureC.Should().Be(26);
        sample.RemainingMinutes.Should().BeNull();
        sample.PublicSessionActive.Should().BeTrue();
    }

    [TestMethod]
    public void DecodeSample_LeavesMissingFieldsNull()
    {
        var sample = SmartShuntPublicProtocol.DecodeSample(new Dictionary<string, byte[]>(), DateTime.UtcNow);

        sample.StateOfChargePercent.Should().BeNull();
        sample.VoltageV.Should().BeNull();
        sample.CurrentA.Should().BeNull();
        sample.PowerW.Should().BeNull();
        sample.ConsumedAh.Should().BeNull();
        sample.StarterVoltageV.Should().BeNull();
        sample.TemperatureC.Should().BeNull();
        sample.RemainingMinutes.Should().BeNull();
    }

    [TestMethod]
    public void DecodeSample_PreservesNegativeDischargeSigns()
    {
        var sample = SmartShuntPublicProtocol.DecodeSample(new Dictionary<string, byte[]>
        {
            ["current"] = BitConverter.GetBytes(-5_000),
            ["power"] = BitConverter.GetBytes((short)-260),
        }, DateTime.UtcNow);

        sample.CurrentA.Should().Be(-5);
        sample.PowerW.Should().Be(-260);
    }
}
