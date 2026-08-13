using FluentAssertions;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.SmartShunt;

namespace HVO.Hardware.VictronSmartShunt.Tests.SmartShunt;

[TestClass]
public sealed class SmartShuntPowerMapperTests
{
    [TestMethod]
    [DataRow(12.5, 668.0)]
    [DataRow(-12.5, -668.0)]
    public void Mapping_PreservesSourceNativeCurrentAndPowerSigns(double current, double power)
    {
        var sample = new SmartShuntLiveSample { RecordedAtUtc = DateTime.UtcNow, CurrentA = current, PowerW = power, ConsumedAh = -42, RemainingMinutes = 120 };
        var options = new SmartShuntOptions { SourceId = "source", DeviceId = "device" };

        var summary = SmartShuntPowerMapper.MapSummary(sample, options);
        var detail = SmartShuntPowerMapper.MapDetail(sample, options);

        summary.BatteryCurrentA.Should().Be(current);
        summary.BatteryPowerW.Should().Be(power);
        summary.SourceSystem.Should().Be("victron-smartshunt");
        detail.ConsumedAh.Should().Be(-42);
        detail.RemainingMinutes.Should().Be(120);
        detail.RecordedAtUtc.Should().Be(summary.RecordedAtUtc);
    }
}
