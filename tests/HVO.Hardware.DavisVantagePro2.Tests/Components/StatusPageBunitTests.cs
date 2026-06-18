using System.Diagnostics;
using System.Reflection;
using Bunit;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Outbox;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Components.Pages;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Station.Models;
using HVO.Hardware.DavisVantagePro2.Telemetry;
using HVO.Hardware.DavisVantagePro2.Workers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

namespace HVO.Hardware.DavisVantagePro2.Tests.Components;

[TestClass]
public sealed class StatusPageBunitTests : BunitContext
{
    [TestMethod]
    public void RendersLiveStatusCards()
    {
        DavisComponentTestServices.RegisterDavisServices(Services, DavisComponentTestServices.CreateLiveReading());

        var component = Render<Status>();

        component.Markup.Should().Contain("Station Conditions");
        component.Markup.Should().Contain("72.4");
        component.Markup.Should().Contain("Inside Temperature");
        component.Markup.Should().Contain("Wind Compass");
        component.Markup.Should().Contain("Rain Rate");
    }

    [TestMethod]
    public void RendersChartsWithStableIds()
    {
        DavisComponentTestServices.RegisterDavisServices(Services, DavisComponentTestServices.CreateLiveReading());

        var component = Render<Status>();

        component.Find("#status-temp-chart").Should().NotBeNull();
        component.Find("#status-wind-chart").Should().NotBeNull();
        component.Find("#status-solar-chart").Should().NotBeNull();
        component.Find("#status-astro-chart").Should().NotBeNull();
    }

    [TestMethod]
    public void RendersSensorFallbacksWhenDataMissing()
    {
        DavisComponentTestServices.RegisterDavisServices(Services, new Loop2Packet
        {
            RecordedAtUtc = DateTime.UtcNow,
        });

        var component = Render<Status>();

        component.Markup.Should().Contain("Waiting for current console forecast.");
        component.Markup.Should().Contain("--°F");
        component.Markup.Should().Contain("--%");
        component.Markup.Should().Contain("-- mph");
        component.Markup.Should().Contain("--");
    }
}

internal static class DavisComponentTestServices
{
    public static void RegisterDavisServices(IServiceCollection services, Loop2Packet? latestReading = null)
    {
        services.AddLogging();
        services.AddMudServices();

        var station = CreateStation();
        var worker = CreateWorker(station, latestReading);
        var forwarder = CreateForwarder();
        var siteState = new HVO.Hardware.DavisVantagePro2.Services.DavisSiteState(
            station,
            worker,
            forwarder,
            new StationInfoSnapshotStore(CreateScopeFactory()),
            NullLogger<HVO.Hardware.DavisVantagePro2.Services.DavisSiteState>.Instance);

        SetProperty(siteState, "IsInitialized", true);
        SetProperty(siteState, "StationInfo", CreateStationInfo());
        SetProperty(siteState, "StationInfoSavedAtUtc", DateTime.UtcNow);

        services.AddSingleton(station);
        services.AddSingleton(worker);
        services.AddSingleton(forwarder);
        services.AddSingleton(siteState);
        services.AddSingleton(new StationSettingsSnapshotStore(CreateScopeFactory()));
    }

    public static Loop2Packet CreateLiveReading() => new()
    {
        RecordedAtUtc = DateTime.UtcNow,
        BarometricPressureInHg = 29.982,
        PressureRawInHg = 28.991,
        OutsideTemperatureF = 72.4,
        InsideTemperatureF = 70.1,
        OutsideHumidityPercent = 28,
        InsideHumidityPercent = 34,
        DewPointF = 37.8,
        HeatIndexF = 72.4,
        ThswF = 73.2,
        WindSpeedMph = 6.2,
        WindDirectionDegrees = 225,
        WindSpeed2MinAvgMph = 5.8,
        WindSpeed10MinAvgMph = 5.1,
        WindGust10MinMph = 12.3,
        WindGust10MinDirectionDegrees = 240,
        RainRateInchesPerHour = 0,
        Rain15MinInches = 0,
        DailyRainInches = 0.01,
        StormRainInches = 0.12,
        MonthlyRainInches = 0.44,
        YearlyRainInches = 4.21,
        DailyEtInches = 0.05,
        SolarRadiationWm2 = 612,
        UvIndex = 3.4,
        BarometricTrend = 20,
        ForecastRule = 1,
        SunriseTime = 531,
        SunsetTime = 1938,
    };

    public static ArchiveRecord CreateArchiveRecord() => new()
    {
        DateTimeLocal = new DateTime(2026, 6, 17, 8, 30, 0),
        ArchiveIntervalMinutes = 5,
        OutsideTemperatureF = 71.2,
        HighOutsideTemperatureF = 73.1,
        LowOutsideTemperatureF = 69.8,
        InsideTemperatureF = 70.4,
        OutsideHumidityPercent = 29,
        InsideHumidityPercent = 35,
        BarometricPressureInHg = 29.981,
        WindSpeedMph = 6,
        WindGustMph = 11,
        RainInches = 0.01,
    };

    public static HVO.Hardware.DavisVantagePro2.Station.Models.StationInfo CreateStationInfo() => new()
    {
        HardwareName = "Davis Vantage Pro2",
        HardwareType = 16,
        ModelType = 2,
        FirmwareVersion = "3.15",
        FirmwareDate = "Mar 29 2013",
        ConsoleTime = new DateTime(2026, 6, 17, 8, 30, 0),
    };

    public static void SetField<T>(IRenderedComponent<T> component, string fieldName, object? value) where T : IComponent
    {
        var field = typeof(T).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(component.Instance, value);
    }

    private static VantageStation CreateStation()
    {
        var station = new VantageStation(
            new DavisConsoleClient("127.0.0.1", 1, TimeSpan.FromMilliseconds(1), NullLogger<DavisConsoleClient>.Instance),
            NullLogger<VantageStation>.Instance);
        station.ApplyStationSettings(new StationSettings
        {
            ArchiveIntervalSeconds = 300,
            LatitudeDegrees = 35.1,
            LongitudeDegrees = -113.9,
            AltitudeFeet = 3491,
            RainYearStartMonth = 1,
            RainBucketType = DavisProtocol.BucketType001Inch,
            DstSetting = "AUTO",
            UseTimezoneCode = true,
            TimezoneCode = 13,
            TemperatureLogging = "LAST",
            BarometerUnits = "inHg",
            TemperatureUnits = "°F×10",
            RainUnits = "inch",
            WindUnits = "mph",
        });

        return station;
    }

    private static WeatherStationWorker CreateWorker(VantageStation station, Loop2Packet? latestReading)
    {
        var worker = new WeatherStationWorker(
            station,
            CreateScopeFactory(),
            Options.Create(new StationOptions()),
            new DavisTelemetry(),
            new NoOpTelemetryService(),
            NullLogger<WeatherStationWorker>.Instance);

        if (latestReading is not null)
        {
            SetProperty(worker, "LatestReading", latestReading);
            SetProperty(worker, "LastReadingAt", latestReading.RecordedAtUtc);
        }

        return worker;
    }

    private static OutboxForwarder CreateForwarder() => new(
        CreateScopeFactory(),
        new StubHttpClientFactory(),
        Options.Create(new OutboxOptions { ApiEndpoint = "http://127.0.0.1/weather" }),
        new DavisTelemetry(),
        new NoOpTelemetryService(),
        NullLogger<OutboxForwarder>.Instance);

    private static IServiceScopeFactory CreateScopeFactory() => new ServiceCollection()
        .AddLogging()
        .BuildServiceProvider()
        .GetRequiredService<IServiceScopeFactory>();

    private static void SetProperty(object instance, string propertyName, object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property.Should().NotBeNull();
        property!.SetValue(instance, value);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class NoOpTelemetryService : ITelemetryService
    {
        public bool IsEnabled => false;
        public ITelemetryStatistics Statistics { get; } = new NoOpTelemetryStatistics();
        public IOperationScope StartOperation(string operationName) => new NoOpOperationScope(operationName);
        public void TrackException(Exception exception) { }
        public void TrackEvent(string eventName) { }
        public void RecordMetric(string metricName, double value) { }
        public void Start() { }
        public void Shutdown() { }
    }

    private sealed class NoOpOperationScope(string name) : IOperationScope
    {
        public string Name { get; } = name;
        public string CorrelationId { get; } = string.Empty;
        public Activity? Activity => null;
        public TimeSpan Elapsed => TimeSpan.Zero;
        public IOperationScope WithTag(string key, object? value) => this;
        public IOperationScope WithTags(IEnumerable<KeyValuePair<string, object?>> tags) => this;
        public IOperationScope WithProperty(string key, Func<object?> valueFactory) => this;
        public IOperationScope Fail(Exception exception) => this;
        public IOperationScope Succeed() => this;
        public IOperationScope WithResult(object? result) => this;
        public IOperationScope CreateChild(string name) => new NoOpOperationScope(name);
        public void RecordException(Exception exception) { }
        public void Dispose() { }
    }

    private sealed class NoOpTelemetryStatistics : ITelemetryStatistics
    {
        public DateTimeOffset StartTime { get; } = DateTimeOffset.UtcNow;
        public long ActivitiesCreated => 0;
        public long ActivitiesCompleted => 0;
        public long ActiveActivities => 0;
        public long ExceptionsTracked => 0;
        public long EventsRecorded => 0;
        public long MetricsRecorded => 0;
        public int QueueDepth => 0;
        public int MaxQueueDepth => 0;
        public long ItemsEnqueued => 0;
        public long ItemsProcessed => 0;
        public long ItemsDropped => 0;
        public long ProcessingErrors => 0;
        public double AverageProcessingTimeMs => 0;
        public long CorrelationIdsGenerated => 0;
        public double CurrentErrorRate => 0;
        public double CurrentThroughput => 0;
        public IReadOnlyDictionary<string, ActivitySourceStatistics> PerSourceStatistics { get; } = new Dictionary<string, ActivitySourceStatistics>();
        public TelemetryStatisticsSnapshot GetSnapshot() => new() { Timestamp = DateTimeOffset.UtcNow, StartTime = StartTime };
        public void Reset() { }
    }
}
