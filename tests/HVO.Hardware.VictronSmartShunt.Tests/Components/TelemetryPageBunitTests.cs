using System.Reflection;
using Bunit;
using FluentAssertions;
using HVO.Edge.Outbox;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.Outbox;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using HVO.Hardware.VictronSmartShunt.SmartShunt.Health;
using HVO.Hardware.VictronSmartShunt.Telemetry;
using HVO.Hardware.VictronSmartShunt.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

namespace HVO.Hardware.VictronSmartShunt.Tests.Components;

[TestClass]
public sealed class TelemetryPageBunitTests : BunitContext
{
    [TestMethod]
    public void RendersPrivateMetadataOverlay()
    {
        Services.AddMudServices();
        Services.AddHttpClient();
        var options = new SmartShuntOptions { Address = "D0:39:72:AA:BB:CC", EnablePrivateEnrichment = true, PublicOnly = false };
        var worker = CreateWorker(options);
        SetWorkerSnapshot(worker, CreateSnapshot(), new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc));
        SetPrivateInfo(worker, new SmartShuntDeviceInfo
        {
            FirmwareVersion = "v1.42",
            SerialNumber = "HQ12345",
            ProductId = "0xA389",
            ProductFamilyRaw = "0x09",
            ProductMetadata = "SmartShunt 500A",
            ProductMetadataRaw = "raw-metadata",
            RecordedAtUtc = new DateTime(2026, 6, 17, 12, 1, 0, DateTimeKind.Utc),
        });
        RegisterServices(worker, options);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var component = Render<HVO.Hardware.VictronSmartShunt.Components.Pages.Telemetry>();

        component.Markup.Should().Contain("Private device metadata");
        component.Markup.Should().Contain("v1.42");
        component.Markup.Should().Contain("HQ12345");
        component.Markup.Should().Contain("SmartShunt 500A");
    }

    [TestMethod]
    public void RendersTelemetryChart()
    {
        Services.AddMudServices();
        Services.AddHttpClient();
        var options = new SmartShuntOptions { Address = "D0:39:72:AA:BB:CC", EnablePrivateEnrichment = true, PublicOnly = false };
        var worker = CreateWorker(options);
        SetWorkerSnapshot(worker, CreateSnapshot(), new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc));
        RegisterServices(worker, options);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var component = Render<HVO.Hardware.VictronSmartShunt.Components.Pages.Telemetry>();

        component.Markup.Should().Contain("Current Telemetry Snapshot");
        component.Find("#smartshunt-telemetry-chart").Should().NotBeNull();
    }

    [TestMethod]
    public void RendersMissingPrivateValuesAsFallback()
    {
        Services.AddMudServices();
        Services.AddHttpClient();
        var options = new SmartShuntOptions { Address = "D0:39:72:AA:BB:CC", EnablePrivateEnrichment = false, PublicOnly = true };
        var worker = CreateWorker(options);
        SetWorkerSnapshot(worker, CreateSnapshot(), new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc));
        SetPrivateInfo(worker, new SmartShuntDeviceInfo());
        RegisterServices(worker, options);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var component = Render<HVO.Hardware.VictronSmartShunt.Components.Pages.Telemetry>();

        component.Markup.Should().Contain("Private disabled");
        component.Markup.Should().Contain("Private device metadata");
        component.Markup.Should().Contain("History overlay");
    }

    private void RegisterServices(SmartShuntWorker worker, SmartShuntOptions options)
    {
        var forwarder = CreateForwarder();
        Services.AddSingleton(worker);
        Services.AddSingleton(forwarder);
        Services.AddSingleton(Options.Create(options));
        Services.AddSingleton(new SmartShuntGatewayHealthService(worker, forwarder, Options.Create(options), Options.Create(new OutboxOptions())));
    }

    private static SmartShuntDeviceSnapshot CreateSnapshot()
        => new()
        {
            RecordedAtUtc = new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc),
            DataPath = "public+private",
            PublicSessionActive = true,
            PrivateEnrichmentActive = true,
            StateOfChargePercent = 82,
            VoltageV = 53.2,
            CurrentA = -2.35,
            PowerW = -125,
            ConsumedAh = -18.4,
            DeepestDischargeAh = 72.4,
            TotalChargeCycles = 184,
            MinBatteryVoltageV = 47.8,
            ChargedEnergyKwh = 1250.5,
        };

    private static SmartShuntWorker CreateWorker(SmartShuntOptions options)
        => new(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), new FakeSessionState(), new FakePrivateInfoSource(), Options.Create(options), NullLogger<SmartShuntWorker>.Instance, new SmartShuntTelemetry());

    private static PowerApiForwarder CreateForwarder()
        => new(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), new FakeHttpClientFactory(), Options.Create(new OutboxOptions()), new RuntimeOutboxSettings(), NullLogger<PowerApiForwarder>.Instance, new SmartShuntTelemetry());

    private static void SetWorkerSnapshot(SmartShuntWorker worker, SmartShuntDeviceSnapshot snapshot, DateTime recordedAtUtc)
    {
        typeof(SmartShuntWorker).GetField("_lastSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(worker, snapshot);
        typeof(SmartShuntWorker).GetField("_lastSnapshotAtTicks", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(worker, recordedAtUtc.Ticks);
    }

    private static void SetPrivateInfo(SmartShuntWorker worker, SmartShuntDeviceInfo info)
        => typeof(SmartShuntWorker).GetField("_privateInfo", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(worker, info);

    private sealed class FakeSessionState : ISmartShuntSessionState
    {
        public SmartShuntLiveSample? CurrentSample => null;
    }

    private sealed class FakePrivateInfoSource : ISmartShuntPrivateInfoSource
    {
        public Task<SmartShuntDeviceInfo?> TryReadAsync(CancellationToken ct) => Task.FromResult<SmartShuntDeviceInfo?>(null);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
