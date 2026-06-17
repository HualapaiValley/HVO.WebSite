using Bunit;
using FluentAssertions;
using HVO.Gateway.SolarAssistant.Components.Pages;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;
using HVO.Gateway.SolarAssistant.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

namespace HVO.Gateway.SolarAssistant.Tests.Components;

[TestClass]
public sealed class MqttInventoryPageBunitTests : BunitContext
{
    public MqttInventoryPageBunitTests() => Services.AddMudServices();

    [TestMethod]
    public void RendersInventoryJsonOrTableState()
    {
        var store = new SolarAssistantMqttInventoryStore();
        store.MarkConnected();
        store.Apply(new SolarAssistantMqttMessage { Topic = "homeassistant/sensor/solar_load/config", Payload = "{\"name\":\"Load Power\",\"stat_t\":\"solar_assistant/total/load_power/state\",\"unit_of_meas\":\"W\",\"dev\":{\"name\":\"SolarAssistant\"}}", ReceivedAtUtc = DateTime.UtcNow });
        store.Apply(new SolarAssistantMqttMessage { Topic = "solar_assistant/total/load_power/state", Payload = "620", ReceivedAtUtc = DateTime.UtcNow });
        Services.AddSingleton(new SolarAssistantMqttDiscoveryWorker(Options.Create(new SolarAssistantOptions()), store, NullLogger<SolarAssistantMqttDiscoveryWorker>.Instance));

        var component = Render<MqttInventory>();

        component.Markup.Should().Contain("Home Assistant Entities");
        component.Markup.Should().Contain("Load Power");
        component.Markup.Should().Contain("solar_assistant/total/load_power/state");
        component.Markup.Should().Contain("MQTT State Topics");
    }
}
