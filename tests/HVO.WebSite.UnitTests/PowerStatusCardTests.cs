using Bunit;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Components.Pages;
using HVO.WebSite.v9.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class PowerStatusCardTests : Bunit.TestContext
{
    [TestMethod]
    public void PowerStatusCard_RendersSnapshotValues()
    {
        var observedAt = new DateTime(2026, 5, 27, 19, 35, 0, DateTimeKind.Utc);
        Services.AddSingleton<IPowerSystemSnapshotProvider>(new StubPowerSystemSnapshotProvider(
            new PowerSystemSnapshot(
                ObservedAtUtc: observedAt,
                Ac: new PowerSystemAcSnapshot(
                    LoadPowerW: Value(474d, PowerMetricSource.SolarAssistant, observedAt),
                    GridPowerW: Value(0d, PowerMetricSource.SolarAssistant, observedAt),
                    GridFlowDirection: Value(PowerFlowDirection.Idle, PowerMetricSource.SolarAssistant, observedAt),
                    InverterMode: new SourcedValue<string>("Solar/Battery", PowerMetricSource.SolarAssistant, observedAt)),
                Pv: new PowerSystemPvSnapshot(Value(3098d, PowerMetricSource.SolarAssistant, observedAt)),
                Battery: new PowerSystemBatterySnapshot(
                    StateOfChargePercent: Value(100d, PowerMetricSource.SolarAssistant, observedAt),
                    PowerW: Value(-900d, PowerMetricSource.VictronSmartShunt, observedAt),
                    FlowDirection: Value(PowerFlowDirection.Charging, PowerMetricSource.VictronSmartShunt, observedAt)))));

        var component = RenderComponent<PowerStatusCard>();

        component.Markup.Should().Contain("Live Power Snapshot");
        component.Markup.Should().Contain("3098 W");
        component.Markup.Should().Contain("-900 W");
        component.Markup.Should().Contain("Charging");
        component.Markup.Should().Contain("SmartShunt");
    }

    private static SourcedValue<T> Value<T>(T value, PowerMetricSource source, DateTime recordedAt)
        => new(value, source, recordedAt);

    private sealed class StubPowerSystemSnapshotProvider(PowerSystemSnapshot? snapshot) : IPowerSystemSnapshotProvider
    {
        public Task<PowerSystemSnapshot?> GetLatestAsync(int lookbackMinutes = 60, CancellationToken ct = default)
            => Task.FromResult(snapshot);
    }
}
