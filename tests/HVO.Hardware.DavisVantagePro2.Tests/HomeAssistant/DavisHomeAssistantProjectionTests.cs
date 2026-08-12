using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.HomeAssistant;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Tests.HomeAssistant;

[TestClass]
public sealed class DavisHomeAssistantProjectionTests
{
    [TestMethod]
    public void Projection_UsesStableIdentityBoundedLiveStateAndAvailability()
    {
        var mqtt = new CaptureProjection();
        var projection = new DavisHomeAssistantProjection(mqtt, Identity("hvo"), Options.Create(new StationOptions { StationId = "station-1" }));
        var at = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

        projection.Publish(new Loop2Packet { RecordedAtUtc = at, OutsideTemperatureF = 72, OutsideHumidityPercent = 30, WindSpeedMph = 5 });
        projection.PublishUnavailable(new DateTimeOffset(at.AddMinutes(1)));

        mqtt.Definition!.Key.Should().Be(new HomeAssistantDeviceKey("hvo", "davis", "station-1"));
        mqtt.Definition.Entities.Should().HaveCount(13);
        mqtt.States[0].Available.Should().BeTrue();
        mqtt.States[0].ComponentValues.Keys.Should().BeEquivalentTo(["outside_temperature", "outside_humidity", "wind_speed"]);
        mqtt.States[1].Available.Should().BeFalse();
    }

    [TestMethod]
    public void Projection_RequiresExplicitSiteId()
    {
        var action = () => new DavisHomeAssistantProjection(new CaptureProjection(), Identity(null), Options.Create(new StationOptions { StationId = "station-1" }));
        action.Should().Throw<InvalidOperationException>().WithMessage("*SiteId*");
    }

    private static EdgeRuntimeIdentity Identity(string? siteId) => new(
        "hvo-davis", "1", "instance", "davis", "davis-vantage-pro2", GatewayDomain.Weather,
        "station-1", siteId, "station-1", "Testing", "host", "Davis");

    private sealed class CaptureProjection : IHomeAssistantMqttProjection
    {
        public HomeAssistantDeviceDefinition? Definition { get; private set; }
        public List<HomeAssistantCurrentState> States { get; } = [];
        public void UpsertDevice(HomeAssistantDeviceDefinition definition) => Definition = definition;
        public bool PublishCurrentState(HomeAssistantCurrentState state) { States.Add(state); return true; }
        public bool RemoveDevice(HomeAssistantDeviceKey key) => false;
        public HomeAssistantMqttStatus GetStatus() => new(true, true, Definition is null ? 0 : 1, null, null, null);
    }
}
