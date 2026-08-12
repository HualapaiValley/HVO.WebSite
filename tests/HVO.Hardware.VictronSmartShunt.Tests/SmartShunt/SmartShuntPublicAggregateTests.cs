using FluentAssertions;
using HVO.Hardware.VictronSmartShunt.SmartShunt;

namespace HVO.Hardware.VictronSmartShunt.Tests.SmartShunt;

[TestClass]
public sealed class SmartShuntPublicAggregateTests
{
    [TestMethod]
    public void Aggregate_WaitsForAllRequiredFieldsAndUsesOldestRequiredTimestamp()
    {
        var aggregate = new SmartShuntPublicAggregate();
        var start = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);

        aggregate.Update("voltage", BitConverter.GetBytes((short)5200), start).Should().BeNull();
        aggregate.Update("current", BitConverter.GetBytes(5_000), start.AddSeconds(1)).Should().BeNull();
        aggregate.Update("power", BitConverter.GetBytes((short)260), start.AddSeconds(2)).Should().BeNull();
        var sample = aggregate.Update("soc", BitConverter.GetBytes((ushort)8000), start.AddSeconds(3));

        sample.Should().NotBeNull();
        sample!.RecordedAtUtc.Should().Be(start);
    }

    [TestMethod]
    public void Aggregate_UnrelatedUpdatesDoNotRefreshRequiredFreshnessAndResetClearsState()
    {
        var aggregate = new SmartShuntPublicAggregate();
        var start = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);
        aggregate.Update("voltage", BitConverter.GetBytes((short)5200), start);
        aggregate.Update("current", BitConverter.GetBytes(5_000), start.AddSeconds(1));
        aggregate.Update("power", BitConverter.GetBytes((short)260), start.AddSeconds(2));
        aggregate.Update("soc", BitConverter.GetBytes((ushort)8000), start.AddSeconds(3));

        aggregate.Update("temperature", BitConverter.GetBytes((short)25), start.AddMinutes(10))!.RecordedAtUtc.Should().Be(start);
        aggregate.Reset();
        aggregate.CurrentSample().Should().BeNull();
    }
}
