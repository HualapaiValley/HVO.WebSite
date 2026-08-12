using FluentAssertions;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class HomeAssistantKasaDashboardTests
{
    private static readonly string[] RenderedStatusOnlySwitches =
    [
        "switch.tp_link_smart_plug_37ec_observatory_roof_master",
        "switch.tp_link_smart_plug_35b6_observatory_roof_controller",
        "switch.tp_link_smart_plug_35b6_observatory_camera",
        "switch.n_roof_allskycamera_n_roof_allsky_camera",
        "switch.n_roof_allskycamera_n_roof_security_camera",
        "switch.north_roof_light_n_roof_wifi",
        "switch.tp_link_power_strip_0628_telescope01_pc",
        "switch.tp_link_power_strip_0628_telescope_01_mx_focuser",
        "switch.tp_link_power_strip_0628_telescope01_camera_6200mm",
        "switch.tp_link_power_strip_0628_telescope_01_flatman",
        "switch.tp_link_smart_plug_97de_kvm_hvo_proxmox_01"
    ];

    private static readonly string[] ProhibitedControlEntities =
    [
        "switch.tp_link_smart_plug_37ec",
        "switch.tp_link_smart_plug_35b6",
        "switch.tp_link_smart_plug_97de",
        "switch.tp_link_power_strip_4540",
        "switch.tpra_smart_switch",
        "switch.workshop_power_strip_plug_1",
        "switch.north_roof_light",
        "switch.n_roof_allskycamera",
        "switch.tp_link_power_strip_d65c",
        "switch.tp_link_power_strip_0628",
        "switch.south_roof_light",
        "switch.tp_link_power_strip_4540_starlink_router",
        "switch.tp_link_power_strip_4540_control_room_router",
        "switch.tp_link_power_strip_4540_hvo_proxmox_2",
        "switch.tp_link_power_strip_4540_control_room_network_switch",
        "switch.tp_link_power_strip_4540_control_room_deco_wifi",
        "switch.tpra_smart_switch_hvo_proxmox_01",
        "switch.tpra_smart_switch_securitycamera07",
        "switch.tpra_smart_switch_workshop_deco"
    ];

    [TestMethod]
    public void CriticalEquipmentCards_DoNotExposeActions()
    {
        var lines = ReadDashboardLines();

        foreach (var entityId in RenderedStatusOnlySwitches)
        {
            var cards = GetCards(lines, entityId);

            cards.Should().NotBeEmpty($"{entityId} must remain represented on the dashboard");
            foreach (var card in cards)
            {
                card.Should().NotContain("action: toggle", $"{entityId} is status-only");
                card.Count(line => line.Trim() == "action: none").Should().Be(4,
                    $"{entityId} must disable tap, icon tap, hold, and double tap actions");
            }
        }
    }

    [TestMethod]
    public void ActionableEquipment_AlwaysRequiresConfirmation()
    {
        var lines = ReadDashboardLines();
        var toggleIndexes = lines
            .Select((line, index) => (Line: line.Trim(), Index: index))
            .Where(item => item.Line == "action: toggle")
            .Select(item => item.Index)
            .ToArray();

        toggleIndexes.Should().NotBeEmpty();
        foreach (var index in toggleIndexes)
        {
            lines[index + 1].Trim().Should().Be("confirmation:",
                $"the toggle at dashboard line {index + 1} must require explicit confirmation");
        }
    }

    [TestMethod]
    public void ParentAndCriticalInfrastructureControls_AreNotExposed()
    {
        var entityDeclarations = GetEntityDeclarations(ReadDashboardLines());

        foreach (var entityId in ProhibitedControlEntities)
        {
            entityDeclarations.Should().NotContain($"entity: {entityId}",
                "parent and critical infrastructure controls are status-only on this dashboard");
        }
    }

    [TestMethod]
    public void EntityDeclarations_IncludeCardsAndEntityRows()
    {
        var declarations = GetEntityDeclarations(
        [
            "    entity: switch.card_entity",
            "      - entity: switch.entity_row"
        ]);

        declarations.Should().BeEquivalentTo(
            "entity: switch.card_entity",
            "entity: switch.entity_row");
    }

    [TestMethod]
    public void Dashboard_DoesNotLoadExternalResourcesOrSecrets()
    {
        var dashboard = string.Join('\n', ReadDashboardLines());

        dashboard.Should().NotContain("http://");
        dashboard.Should().NotContain("https://");
        dashboard.Should().NotContain("token");
        dashboard.Should().NotContain("password");
    }

    [TestMethod]
    public void ManagedConfiguration_UsesSupportedIncludesAndNoExternalResources()
    {
        var configurationRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "HomeAssistant", "Configuration");
        var lovelace = File.ReadAllText(Path.Combine(configurationRoot, "lovelace.yaml"));
        var package = File.ReadAllText(Path.Combine(configurationRoot, "packages", "hvo.yaml"));
        var proxy = File.ReadAllText(Path.Combine(configurationRoot, "esphome", "hvo-bluetooth-proxy.yaml"));
        var managedConfiguration = Directory.GetFiles(configurationRoot, "*.yaml", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        lovelace.Should().Contain("filename: hvo/dashboards/hvo-kasa.yaml");
        package.Should().Contain("template: !include ../templates/hvo.yaml");
        package.Should().Contain("automation: !include ../automations/hvo.yaml");
        proxy.Should().Contain("bluetooth_proxy:");
        proxy.Should().Contain("active: true");
        proxy.Should().Contain("name: home-dev-bluetooth-proxy");
        proxy.Should().Contain("friendly_name: Home Dev Bluetooth Proxy");
        proxy.Should().Contain("key: !secret hvo_bluetooth_proxy_api_encryption_key");
        proxy.Should().Contain("ssid: !secret hvo_wifi_ssid");
        proxy.Should().Contain("password: !secret hvo_wifi_password");
        proxy.Should().Contain("priority: 2");
        proxy.Should().Contain("ssid: !secret home_wifi_ssid");
        proxy.Should().Contain("password: !secret home_wifi_password");
        proxy.Should().Contain("priority: 1");
        managedConfiguration.Should().OnlyContain(content => !content.Contains(".storage", StringComparison.Ordinal));
        managedConfiguration.Should().OnlyContain(content => !content.Contains("http://", StringComparison.Ordinal));
        managedConfiguration.Should().OnlyContain(content => !content.Contains("https://", StringComparison.Ordinal));
    }

    private static string[] ReadDashboardLines() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "HomeAssistant", "Configuration", "dashboards", "hvo-kasa.yaml"));

    private static string[] GetEntityDeclarations(IEnumerable<string> lines) =>
        lines
            .Select(line => line.Trim().TrimStart('-').TrimStart())
            .Where(line => line.StartsWith("entity: ", StringComparison.Ordinal))
            .ToArray();

    private static IReadOnlyList<string[]> GetCards(string[] lines, string entityId)
    {
        var cards = new List<string[]>();
        for (var entityIndex = 0; entityIndex < lines.Length; entityIndex++)
        {
            if (lines[entityIndex].Trim() != $"entity: {entityId}")
                continue;

            var cardStart = entityIndex;
            while (cardStart >= 0 && lines[cardStart].Trim() != "- type: tile")
                cardStart--;

            cardStart.Should().BeGreaterThanOrEqualTo(0);
            var cardIndent = lines[cardStart].TakeWhile(char.IsWhiteSpace).Count();
            var cardEnd = entityIndex + 1;
            while (cardEnd < lines.Length)
            {
                var current = lines[cardEnd];
                var currentIndent = current.TakeWhile(char.IsWhiteSpace).Count();
                if (current.TrimStart().StartsWith("- type:", StringComparison.Ordinal) && currentIndent <= cardIndent)
                    break;

                cardEnd++;
            }

            cards.Add(lines[cardStart..cardEnd]);
        }

        return cards;
    }
}
