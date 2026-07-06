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
using Microsoft.JSInterop;
using MudBlazor.Services;

namespace HVO.Hardware.VictronSmartShunt.Tests.Components;

[TestClass]
public sealed class SmartShuntStatusPageBunitTests : BunitContext
{
    public SmartShuntStatusPageBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [TestMethod]
    public void RendersCurrentSnapshotAndHealth()
    {
        Services.AddMudServices();
        Services.AddHttpClient();
        var options = new SmartShuntOptions { Address = "D0:39:72:AA:BB:CC", EnablePrivateEnrichment = true, PublicOnly = false };
        var worker = CreateWorker(options);
        SetWorkerSnapshot(worker, CreateSnapshot(), DateTime.UtcNow);
        Services.AddSingleton(worker);
        var forwarder = CreateForwarder();
        Services.AddSingleton(forwarder);
        Services.AddSingleton(Options.Create(options));
        Services.AddSingleton(CreateHealthService(worker, forwarder, options));

        var component = Render<HVO.Hardware.VictronSmartShunt.Components.Pages.Status>();

        component.Markup.Should().Contain("Victron SmartShunt overview");
        component.Markup.Should().Contain("Healthy");
        component.Markup.Should().Contain("82 %");
        component.Markup.Should().Contain("53.20 V");
        component.Markup.Should().Contain("-125 W");
    }

    [TestMethod]
    public void RendersFallbacksWhenSnapshotMissing()
    {
        Services.AddMudServices();
        Services.AddHttpClient();
        var options = new SmartShuntOptions { Address = "D0:39:72:AA:BB:CC", EnablePrivateEnrichment = false };
        var worker = CreateWorker(options);
        var forwarder = CreateForwarder();
        Services.AddSingleton(worker);
        Services.AddSingleton(forwarder);
        Services.AddSingleton(Options.Create(options));
        Services.AddSingleton(CreateHealthService(worker, forwarder, options));

        var component = Render<HVO.Hardware.VictronSmartShunt.Components.Pages.Status>();

        component.Markup.Should().Contain("Victron SmartShunt overview");
        component.Markup.Should().Contain("Warning");
        component.Markup.Should().Contain("--");
        component.Markup.Should().Contain("Disabled by configuration");
        component.Markup.Should().Contain("Waiting for first SmartShunt sample.");
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
            RemainingMinutes = 420,
        };

    private static SmartShuntWorker CreateWorker(SmartShuntOptions options)
        => new(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), new FakeSessionState(), new FakePrivateInfoSource(), Options.Create(options), NullLogger<SmartShuntWorker>.Instance, new SmartShuntTelemetry());

    private static PowerApiForwarder CreateForwarder()
        => new(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), new FakeHttpClientFactory(), Options.Create(new OutboxOptions()), new RuntimeOutboxSettings(), NullLogger<PowerApiForwarder>.Instance, new SmartShuntTelemetry());

    private static SmartShuntGatewayHealthService CreateHealthService(SmartShuntWorker worker, PowerApiForwarder forwarder, SmartShuntOptions options)
        => new(worker, forwarder, Options.Create(options), Options.Create(new OutboxOptions()));

    private static void SetWorkerSnapshot(SmartShuntWorker worker, SmartShuntDeviceSnapshot snapshot, DateTime recordedAtUtc)
    {
        typeof(SmartShuntWorker).GetField("_lastSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(worker, snapshot);
        typeof(SmartShuntWorker).GetField("_lastSnapshotAtTicks", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(worker, recordedAtUtc.Ticks);
    }

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
