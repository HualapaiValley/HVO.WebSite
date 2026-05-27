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
                    FlowDirection: Value(PowerFlowDirection.Charging, PowerMetricSource.VictronSmartShunt, observedAt),
                    BankCount: Value(1, PowerMetricSource.JkBms, observedAt),
                    HasAlarms: Value(false, PowerMetricSource.JkBms, observedAt)),
                BatteryBanks:
                [
                    new PowerSystemBatteryBankSnapshot(
                        BankId: "bank-1a",
                        RecordedAtUtc: observedAt.AddMinutes(-2),
                        Source: PowerMetricSource.JkBms,
                        StateOfChargePercent: Value(91d, PowerMetricSource.JkBms, observedAt),
                        VoltageV: Value(54.037d, PowerMetricSource.JkBms, observedAt),
                        CurrentA: Value(-2.4d, PowerMetricSource.JkBms, observedAt),
                        DeltaCellVoltageV: Value(0.003d, PowerMetricSource.JkBms, observedAt),
                        HasAlarms: Value(false, PowerMetricSource.JkBms, observedAt)),
                ])));

        var component = RenderComponent<PowerStatusCard>();

        component.Markup.Should().Contain("Live Power Snapshot");
        component.Markup.Should().Contain("3098 W");
        component.Markup.Should().Contain("-900 W");
        component.Markup.Should().Contain("Charging");
        component.Markup.Should().Contain("SmartShunt");
        component.Markup.Should().Contain("bank-1a");
        component.Find("article.power-bank").ClassList.Should().Contain("power-bank--fresh");
        component.Find("article.power-bank").GetAttribute("aria-labelledby").Should().Be("power-bank-1");
        component.Find("article.power-bank h3").TextContent.Should().Be("bank-1a");
        component.Markup.Should().Contain("54.04 V");
        component.Markup.Should().Contain("3 mV");
        component.Markup.Should().Contain("2 min ago");
        component.Markup.Should().Contain("All banks fresh");
        component.Markup.Should().Contain("No alarms");
    }

    private static SourcedValue<T> Value<T>(T value, PowerMetricSource source, DateTime recordedAt)
        => new(value, source, recordedAt);

    private sealed class StubPowerSystemSnapshotProvider(PowerSystemSnapshot? snapshot) : IPowerSystemSnapshotProvider
    {
        public Task<PowerSystemSnapshot?> GetLatestAsync(int lookbackMinutes = 60, CancellationToken ct = default)
            => Task.FromResult(snapshot);
    }
}
