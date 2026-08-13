using FluentAssertions;
using HVO.Hardware.VictronSmartShunt.SmartShunt;

namespace HVO.Hardware.VictronSmartShunt.Tests.SmartShunt;

[TestClass]
public sealed class SmartShuntPublicSessionTests
{
    [TestMethod]
    public void RequiredFields_AreExplicitlyIdentified()
    {
        SmartShuntPublicProtocol.Fields
            .Where(static field => field.IsRequired)
            .Select(static field => field.Key)
            .Should().BeEquivalentTo("voltage", "current", "power", "soc");
    }

    [TestMethod]
    public void RequiredSampleFreshness_UsesOldestRequiredFieldTimestamp()
    {
        var observedAt = new DateTime(2026, 8, 13, 1, 0, 0, DateTimeKind.Utc);
        var sample = new SmartShuntLiveSample { RecordedAtUtc = observedAt };

        SmartShuntPublicSession.IsRequiredSampleStale(sample, observedAt.AddSeconds(60), TimeSpan.FromSeconds(60))
            .Should().BeFalse();
        SmartShuntPublicSession.IsRequiredSampleStale(sample, observedAt.AddSeconds(61), TimeSpan.FromSeconds(60))
            .Should().BeTrue();
    }
}
