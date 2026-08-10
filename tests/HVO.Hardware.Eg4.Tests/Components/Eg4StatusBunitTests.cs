using Bunit;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Components.Pages;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Dashboard;
using HVO.Hardware.Eg4.Protocol;
using HVO.Hardware.Eg4.Simulation;
using HVO.WebSite.Themes.Components.Charts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MudBlazor.Services;

namespace HVO.Hardware.Eg4.Tests.Components;

[TestClass]
public sealed class Eg4StatusBunitTests : BunitContext
{
    public Eg4StatusBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [TestMethod]
    public void EmptyFleet_RendersSafeReadOnlyState()
    {
        Services.AddSingleton<IEg4GatewayDashboardState>(new FakeDashboardState(Snapshot([])));

        var component = Render<Status>();

        component.WaitForAssertion(() => component.Markup.Should().Contain("No configured EG4 devices"));
        component.Markup.Should().Contain("No port scanning is available");
        component.Markup.Should().Contain("exposes no register writes");
    }

    [TestMethod]
    public void MixedFleet_RendersStableIdentityStatesOriginsAndUnavailableValues()
    {
        var now = new DateTime(2026, 8, 9, 15, 0, 0, DateTimeKind.Utc);
        var devices = new[]
        {
            Device("eg4-mppt-b", "Controller B", Eg4DeviceType.ChargeControllerMppt10048Hv, PowerMeasurementRole.ChargeControllerBranch, Eg4DashboardDeviceState.Offline, 0, null, true, "Transport Timeout"),
            Device("eg4-inverter-a", "Inverter A", Eg4DeviceType.Inverter6500Ex, PowerMeasurementRole.InverterBranch, Eg4DashboardDeviceState.Online, 16, now, false, model: "MKS2-6500", firmware: "79.02 / 61.00", soc: 83),
            Device("eg4-mppt-a", "Controller A", Eg4DeviceType.ChargeControllerMppt10048Hv, PowerMeasurementRole.ChargeControllerBranch, Eg4DashboardDeviceState.Degraded, -8, now, true, "Transport Crc"),
        };
        Services.AddSingleton<IEg4GatewayDashboardState>(new FakeDashboardState(Snapshot(devices)));

        var component = Render<Status>();

        component.WaitForAssertion(() => component.FindAll("article.eg4-device-card").Should().HaveCount(3));
        var cards = component.FindAll("article.eg4-device-card");
        cards.Select(card => card.GetAttribute("data-source-id")).Should().Equal("eg4-inverter-a", "eg4-mppt-a", "eg4-mppt-b");
        component.Markup.Should().Contain("1 online").And.Contain("1 degraded").And.Contain("1 offline");
        component.Markup.Should().Contain("Discharging").And.Contain("Charging").And.Contain("Idle");
        component.Markup.Should().Contain("Fresh").And.Contain("Stale").And.Contain("Never observed");
        component.Markup.Should().Contain("MKS2-6500").And.Contain("79.02 / 61.00").And.Contain("Unavailable");
        component.Markup.Should().Contain("Simulated").And.Contain("Battery-facing branch measurement").And.Contain("never the whole battery bus");
        component.Find("[data-source-id='eg4-inverter-a']").TextContent.Should().Contain("-16.0 A").And.Contain("-840 W");
        component.Find("[data-source-id='eg4-mppt-a']").TextContent.Should().Contain("+8.0 A").And.Contain("+420 W");
        component.Markup.Should().NotContain("/dev/serial").And.NotContain("0103");
    }

    [TestMethod]
    public void RichTelemetry_RendersBothInverterTrackersSubtotalAcLoadAndControllerOutput()
    {
        var observedAt = new DateTime(2026, 8, 10, 18, 0, 0, DateTimeKind.Utc);
        var inverter = Device("eg4-inverter-a", "Inverter A", Eg4DeviceType.Inverter6500Ex,
            PowerMeasurementRole.InverterBranch, Eg4DashboardDeviceState.Online, -26, observedAt, false) with
        {
            MpptDetail = new PowerMpptDetailPayload
            {
                SourceId = "eg4-inverter-a", DeviceId = "inverter-a", RecordedAtUtc = observedAt,
                Trackers =
                [
                    new PowerMpptTrackerDetail { TrackerId = "mppt-1", Name = "MPPT 1", VoltageV = 333, CurrentA = 4.1, PowerW = 1378, Provenance = PowerObservationProvenance.Direct },
                    new PowerMpptTrackerDetail { TrackerId = "mppt-2", Name = "MPPT 2", VoltageV = 372.6, CurrentA = 3, PowerW = 1117.8, Provenance = PowerObservationProvenance.Derived },
                ],
            },
            InverterDetail = new PowerInverterDetailPayload
            {
                SourceId = "eg4-inverter-a", DeviceId = "inverter-a", RecordedAtUtc = observedAt,
                Ac = new PowerInverterAcDetail { InputVoltageV = 0, InputFrequencyHz = 0, OutputVoltageV = 120.1, OutputFrequencyHz = 59.9 },
                Load = new PowerInverterLoadDetail { LoadPowerW = 1180, LoadApparentPowerVa = 1270 },
                Operating = new PowerInverterOperatingDetail { Mode = "B", FaultCode = "00", LoadPercentage = 19 },
                Temperatures = [new PowerInverterTemperatureDetail { TemperatureId = "inverter", Name = "Inverter", TemperatureC = 60 }],
            },
        };
        var controller = Device("eg4-mppt-a", "Controller A", Eg4DeviceType.ChargeControllerMppt10048Hv,
            PowerMeasurementRole.ChargeControllerBranch, Eg4DashboardDeviceState.Online, -9.1, observedAt, false) with
        {
            MpptDetail = new PowerMpptDetailPayload
            {
                SourceId = "eg4-mppt-a", DeviceId = "mppt-a", RecordedAtUtc = observedAt,
                Trackers = [new PowerMpptTrackerDetail { TrackerId = "mppt-1", Name = "MPPT 1", VoltageV = 377.2, CurrentA = 2.8, PowerW = 1056, Provenance = PowerObservationProvenance.Direct }],
                BatteryOutput = new PowerMpptBatteryOutputDetail { VoltageV = 54.3, CurrentA = -9.1, PowerW = -494.1 },
            },
        };
        Services.AddSingleton<IEg4GatewayDashboardState>(new FakeDashboardState(Snapshot([inverter, controller])));

        var component = Render<Status>();

        component.WaitForAssertion(() => component.FindAll("[data-tracker-id]").Should().HaveCount(3));
        var inverterCard = component.Find("[data-source-id='eg4-inverter-a']");
        inverterCard.TextContent.Should().Contain("Inverter PV subtotal").And.Contain("2496 W")
            .And.Contain("MPPT 1").And.Contain("MPPT 2").And.Contain("120.1 V / 59.9 Hz")
            .And.Contain("1180 W").And.Contain("1270 VA").And.Contain("+1365 W");
        var controllerCard = component.Find("[data-source-id='eg4-mppt-a']");
        controllerCard.TextContent.Should().Contain("Controller PV").And.Contain("1056 W")
            .And.Contain("Battery charging contribution").And.Contain("+9.1 A").And.Contain("+494 W");
    }

    [TestMethod]
    public void PowerCharts_BucketIndependentDeviceTimestampsOntoSharedMinuteAxis()
    {
        var minute = new DateTime(2026, 8, 10, 18, 0, 0, DateTimeKind.Utc);
        var snapshot = Snapshot([]) with
        {
            History =
            [
                new Eg4DashboardPowerPoint(minute.AddSeconds(10), "eg4-inverter-a/battery", "Inverter battery", Eg4DashboardSeriesKind.Battery, -1000),
                new Eg4DashboardPowerPoint(minute.AddSeconds(45), "eg4-mppt-a/battery", "Controller battery", Eg4DashboardSeriesKind.Battery, -500),
            ],
            HistoryRevision = 2,
        };
        Services.AddSingleton<IEg4GatewayDashboardState>(new FakeDashboardState(snapshot));

        var component = Render<Status>();

        var chart = component.FindComponent<HvoChart>();
        chart.Instance.Labels.Should().ContainSingle();
        chart.Instance.Datasets.Should().HaveCount(2);
        chart.Instance.Datasets.Should().OnlyContain(dataset => dataset.Data.Count == 1 && dataset.Data[0].HasValue);
        chart.Instance.Datasets.Select(dataset => dataset.Data[0]).Should().BeEquivalentTo(new double?[] { 1000, 500 });
    }

    [TestMethod]
    public void UnavailableMppt_RendersReasonWithoutSyntheticZeroReading()
    {
        var controller = new Eg4DashboardDevice(
            "eg4-mppt100-48hv-a",
            "controller-a",
            "MPPT100-48HV Controller A",
            Eg4DeviceType.ChargeControllerMppt10048Hv,
            PowerMeasurementRole.ChargeControllerBranch,
            Eg4DashboardDeviceState.Unavailable);
        Services.AddSingleton<IEg4GatewayDashboardState>(new FakeDashboardState(Snapshot([controller])));

        var component = Render<Status>();

        component.WaitForAssertion(() => component.Markup.Should().Contain("1 telemetry unavailable"));
        var card = component.Find("[data-source-id='eg4-mppt100-48hv-a']");
        card.TextContent.Should().Contain("Telemetry unavailable")
            .And.Contain("not a validated controller-monitoring interface")
            .And.Contain("no numeric reading is reported")
            .And.Contain("Never observed");
        card.TextContent.Should().NotContain("Idle").And.NotContain("0 W").And.NotContain("0.0 A");
    }

    [TestMethod]
    public void OutboxRuntimeControls_ApplyAndResetThroughStateContract()
    {
        var state = new FakeDashboardState(Snapshot([]));
        Services.AddSingleton<IEg4GatewayDashboardState>(state);
        var component = Render<Status>();

        component.FindAll("input").First().Change("100");
        state.NotifyChanged();
        component.WaitForAssertion(() => component.FindAll("input").First().GetAttribute("value").Should().Be("100"));
        component.FindAll("button").Single(button => button.TextContent.Contains("Apply runtime settings", StringComparison.Ordinal)).Click();
        component.WaitForAssertion(() => component.Markup.Should().Contain("Runtime override applied"));
        state.UpdateCount.Should().Be(1);

        component.FindAll("button").Single(button => button.TextContent.Contains("Reset defaults", StringComparison.Ordinal)).Click();
        component.WaitForAssertion(() => component.Markup.Should().Contain("reset to configured defaults"));
        state.UpdateCount.Should().Be(2);
    }

    [TestMethod]
    public void OutboxResetFailure_IsSurfacedWithoutEscapingTheCircuit()
    {
        var snapshot = Snapshot([]) with { Outbox = Snapshot([]).Outbox with { IsOverride = true } };
        var state = new FakeDashboardState(snapshot, throwOnReset: true);
        Services.AddSingleton<IEg4GatewayDashboardState>(state);
        var component = Render<Status>();

        component.FindAll("button").Single(button => button.TextContent.Contains("Reset defaults", StringComparison.Ordinal)).Click();

        component.WaitForAssertion(() => component.Markup.Should().Contain("Outbox reset unavailable"));
    }

    [TestMethod]
    public async Task FleetSimulator_PublishesTransitionsWithoutBrowserDrivenPolling()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 9, 15, 0, 0, TimeSpan.Zero));
        var inverter = Config("eg4-z-inverter", "Z Inverter", Eg4DeviceType.Inverter6500Ex);
        var controller = Config("eg4-a-controller", "A Controller", Eg4DeviceType.ChargeControllerMppt10048Hv);
        var controllerB = Config("eg4-b-controller", "B Controller", Eg4DeviceType.ChargeControllerMppt10048Hv);
        var optionsValue = new Eg4Options { DefaultPollIntervalSeconds = 10, SimulationEnabled = true, Devices = [inverter, controllerB, controller] };
        var options = Options.Create(optionsValue);
        var simulator = new Eg4FleetSimulator(time);
        await simulator.SetScriptAsync(inverter.SourceId, [new Eg4SimulationStep(new Eg4SimulatedTelemetry(52, 8, 416, 80))]);
        await simulator.SetScriptAsync(controller.SourceId, [new Eg4SimulationStep(new Eg4SimulatedTelemetry(52, -5, -260, 75))]);
        await simulator.SetScriptAsync(controllerB.SourceId, [new Eg4SimulationStep(Failure: Eg4TransportFailureKind.Disconnected)]);
        var state = new Eg4GatewayDashboardState(options, time, new UnavailableEg4OutboxDashboardProvider());
        var worker = new Eg4SimulationDashboardWorker(simulator, options, state, time, NullLogger<Eg4SimulationDashboardWorker>.Instance);
        await worker.PublishOnceAsync(CancellationToken.None);
        Services.AddSingleton<IEg4GatewayDashboardState>(state);
        var component = Render<Status>();

        component.WaitForAssertion(() => component.FindAll("article.eg4-device-card")
            .Select(card => card.GetAttribute("data-source-id")).Should().Equal(controller.SourceId, controllerB.SourceId, inverter.SourceId));
        component.Markup.Should().Contain("Charging").And.Contain("Discharging").And.Contain("Transport Disconnected").And.Contain("Never observed");

        optionsValue.Devices.Reverse();
        time.Advance(TimeSpan.FromSeconds(21));
        await simulator.SetScriptAsync(inverter.SourceId, [new Eg4SimulationStep(Failure: Eg4TransportFailureKind.Timeout)]);
        await simulator.SetScriptAsync(controller.SourceId, [new Eg4SimulationStep(new Eg4SimulatedTelemetry(52, 0, 0, 75))]);
        await simulator.SetScriptAsync(controllerB.SourceId, [new Eg4SimulationStep(Failure: Eg4TransportFailureKind.Timeout)]);
        await worker.PublishOnceAsync(CancellationToken.None);

        component.WaitForAssertion(() =>
        {
            component.FindAll("article.eg4-device-card").Select(card => card.GetAttribute("data-source-id"))
                .Should().Equal(controller.SourceId, controllerB.SourceId, inverter.SourceId);
            component.Markup.Should().Contain("Transport Timeout").And.Contain("Stale").And.Contain("Idle");
        });
    }

    private static Eg4DashboardDevice Device(
        string sourceId,
        string alias,
        Eg4DeviceType type,
        PowerMeasurementRole role,
        Eg4DashboardDeviceState state,
        double? current,
        DateTime? observed,
        bool stale,
        string? error = null,
        string? model = null,
        string? firmware = null,
        double? soc = null) => new(
            sourceId,
            sourceId.Replace("eg4-", string.Empty, StringComparison.Ordinal),
            alias,
            type,
            role,
            state,
            observed,
            52.5,
            current,
            current * 52.5,
            soc,
            PowerObservationProvenance.Direct,
            "simulated",
            model,
            firmware,
            error,
            stale);

    private static Eg4GatewayDashboardSnapshot Snapshot(IReadOnlyList<Eg4DashboardDevice> devices) => new(
        devices,
        new Eg4OutboxDashboard(2, 0, null, 0, "Simulation only", null, null, 50, 5, false, true),
        DateTime.SpecifyKind(new DateTime(2026, 8, 9, 15, 0, 0), DateTimeKind.Utc));

    private static Eg4DeviceOptions Config(string sourceId, string alias, Eg4DeviceType type) => new()
    {
        Type = type,
        SourceId = sourceId,
        DeviceId = sourceId.Replace("eg4-", string.Empty, StringComparison.Ordinal),
        Alias = alias,
        Port = $"/dev/serial/by-id/{sourceId}",
        UnitId = 1,
    };

    private sealed class FakeDashboardState(Eg4GatewayDashboardSnapshot snapshot, bool throwOnReset = false) : IEg4GatewayDashboardState
    {
        private Eg4GatewayDashboardSnapshot _snapshot = snapshot;
        public int UpdateCount { get; private set; }
        public event Action? Changed;
        public void NotifyChanged() => Changed?.Invoke();
        public Eg4GatewayDashboardSnapshot GetSnapshot() => _snapshot;
        public Eg4OutboxSettingsResponse UpdateOutboxSettings(Eg4OutboxSettingsUpdate update)
        {
            if (throwOnReset && update.Reset) throw new InvalidOperationException("Outbox reset unavailable.");
            UpdateCount++;
            var reset = update.Reset == true;
            var outbox = _snapshot.Outbox with
            {
                BatchSize = reset ? 50 : update.BatchSize ?? _snapshot.Outbox.BatchSize,
                SweepIntervalSeconds = reset ? 5 : update.SweepIntervalSeconds ?? _snapshot.Outbox.SweepIntervalSeconds,
                IsOverride = !reset,
            };
            _snapshot = _snapshot with { Outbox = outbox };
            Changed?.Invoke();
            return new Eg4OutboxSettingsResponse(outbox.BatchSize, outbox.SweepIntervalSeconds, outbox.IsOverride);
        }
    }
}
