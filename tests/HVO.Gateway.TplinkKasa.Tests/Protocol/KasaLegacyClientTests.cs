using FluentAssertions;
using HVO.Gateway.TplinkKasa.Protocol;
using HVO.Gateway.TplinkKasa.Tests.Fakes;

namespace HVO.Gateway.TplinkKasa.Tests.Protocol;

[TestClass]
public sealed class KasaLegacyClientTests
{
    [TestMethod]
    public async Task SendReadOnlyAsync_ReadsResponseFromFakeServer()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("ep25-sysinfo.json"));
        var client = new KasaLegacyClient(TimeSpan.FromSeconds(2));

        using var response = await client.SendReadOnlyAsync("127.0.0.1", server.Port, KasaCommands.GetSystemInfo, CancellationToken.None);

        response.RootElement.GetProperty("system").GetProperty("get_sysinfo").GetProperty("model").GetString()
            .Should().Be("EP25(US)");
    }

    [TestMethod]
    public async Task SendReadOnlyAsync_RejectsNonAllowlistedCommand()
    {
        var client = new KasaLegacyClient(TimeSpan.FromSeconds(2));

        var act = async () => await client.SendReadOnlyAsync("127.0.0.1", 9999, "{\"system\":{\"set_relay_state\":{\"state\":1}}}", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task SendReadOnlyAsync_RejectsMixedReadAndWriteCommand()
    {
        var client = new KasaLegacyClient(TimeSpan.FromSeconds(2));

        var act = async () => await client.SendReadOnlyAsync(
            "127.0.0.1",
            9999,
            "{\"system\":{\"get_sysinfo\":{},\"set_relay_state\":{\"state\":1}}}",
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task SendReadOnlyAsync_RejectsRefreshingWifiScanCommand()
    {
        var client = new KasaLegacyClient(TimeSpan.FromSeconds(2));

        var act = async () => await client.SendReadOnlyAsync(
            "127.0.0.1",
            9999,
            "{\"netif\":{\"get_scaninfo\":{\"refresh\":1}}}",
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    [DataRow("{\"netif\":{\"get_scaninfo\":null}}")]
    [DataRow("{\"netif\":{\"get_scaninfo\":{}}}")]
    [DataRow("{\"netif\":{\"get_scaninfo\":{\"refresh\":0,\"ssid\":\"private\"}}}")]
    [DataRow("{\"netif\":{\"get_scaninfo\":{\"refresh\":true}}}")]
    [DataRow("{\"netif\":{\"get_scaninfo\":{\"refresh\":\"1\"}}}")]
    public async Task SendReadOnlyAsync_RejectsUnsafeWifiScanParameterVariants(string commandJson)
    {
        var client = new KasaLegacyClient(TimeSpan.FromSeconds(2));

        var act = async () => await client.SendReadOnlyAsync("127.0.0.1", 9999, commandJson, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    [DataRow("{\"netif\":{\"get_scaninfo\":{\"refresh\":0}}}")]
    [DataRow("{\"netif\":{\"get_scaninfo\":{\"refresh\":false}}}")]
    public void IsKnownReadOnly_AllowsExplicitCachedWifiScanOnly(string commandJson)
    {
        using var command = System.Text.Json.JsonDocument.Parse(commandJson);

        KasaCommands.IsKnownReadOnly(command).Should().BeTrue();
    }

    [TestMethod]
    public async Task SendReadOnlyAsync_AllowsCachedWifiScanCommand()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("netif", "get_scaninfo", "{\"netif\":{\"get_scaninfo\":{\"err_code\":0,\"ap_list\":[]}}}");
        var client = new KasaLegacyClient(TimeSpan.FromSeconds(2));

        using var response = await client.SendReadOnlyAsync("127.0.0.1", server.Port, KasaCommands.GetCachedWifiScanInfo, CancellationToken.None);

        response.RootElement.GetProperty("netif").GetProperty("get_scaninfo").GetProperty("err_code").GetInt32()
            .Should().Be(0);
    }

    [TestMethod]
    [DataRow("{\"context\":{\"child_ids\":[\"child-1\"]},\"emeter\":{\"get_realtime\":{}}}")]
    [DataRow("{\"context\":{\"child_ids\":[\"child-1\"]},\"emeter\":{\"get_daystat\":{\"year\":2026,\"month\":6}}}")]
    [DataRow("{\"context\":{\"child_ids\":[\"child-1\"]},\"emeter\":{\"get_monthstat\":{\"year\":2026}}}")]
    [DataRow("{\"context\":{\"child_ids\":[\"child-1\"]},\"schedule\":{\"get_rules\":{}}}")]
    [DataRow("{\"context\":{\"child_ids\":[\"child-1\"]},\"schedule\":{\"get_next_action\":{}}}")]
    [DataRow("{\"context\":{\"child_ids\":[\"child-1\"]},\"count_down\":{\"get_rules\":{}}}")]
    [DataRow("{\"context\":{\"child_ids\":[\"child-1\"]},\"anti_theft\":{\"get_rules\":{}}}")]
    public void IsKnownReadOnly_AllowsSingleChildContextForReadOnlyCommands(string commandJson)
    {
        using var command = System.Text.Json.JsonDocument.Parse(commandJson);

        KasaCommands.IsKnownReadOnly(command).Should().BeTrue();
    }

    [TestMethod]
    [DataRow("{\"context\":{\"child_ids\":[]},\"emeter\":{\"get_realtime\":{}}}")]
    [DataRow("{\"context\":{\"child_ids\":[\"child-1\",\"child-2\"]},\"emeter\":{\"get_realtime\":{}}}")]
    [DataRow("{\"context\":{\"child_ids\":[\"child-1\"]},\"system\":{\"set_relay_state\":{\"state\":1}}}")]
    [DataRow("{\"context\":{\"child_ids\":[\"child-1\"],\"source\":\"unexpected\"},\"emeter\":{\"get_realtime\":{}}}")]
    [DataRow("{\"context\":{\"child_ids\":[\"child-1\"]}}")]
    public async Task SendReadOnlyAsync_RejectsUnsafeChildContextCommands(string commandJson)
    {
        var client = new KasaLegacyClient(TimeSpan.FromSeconds(2));

        var act = async () => await client.SendReadOnlyAsync("127.0.0.1", 9999, commandJson, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task LabClient_AllowsRelayWriteForPlugLab()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "set_relay_state", "{\"system\":{\"set_relay_state\":{\"err_code\":0}}}");
        var client = new KasaLegacyLabClient(TimeSpan.FromSeconds(2));

        using var response = await client.SendAsync("127.0.0.1", server.Port, "{\"system\":{\"set_relay_state\":{\"state\":1}}}", CancellationToken.None);

        response.RootElement.GetProperty("system").GetProperty("set_relay_state").GetProperty("err_code").GetInt32()
            .Should().Be(0);
    }

    [TestMethod]
    [DataRow("system", "set_dev_alias", "{\"system\":{\"set_dev_alias\":{\"alias\":\"Desk Lab\"}}}")]
    [DataRow("system", "reboot", "{\"system\":{\"reboot\":{\"delay\":1}}}")]
    [DataRow("time", "set_timezone", "{\"time\":{\"set_timezone\":{\"year\":2026,\"month\":6,\"mday\":6,\"hour\":12,\"min\":0,\"sec\":0,\"index\":42}}}")]
    [DataRow("schedule", "add_rule", "{\"schedule\":{\"add_rule\":{\"name\":\"HVO Lab\",\"enable\":1,\"wday\":[0,0,0,0,0,0,1],\"repeat\":0,\"sact\":0,\"stime_opt\":0,\"smin\":720,\"soffset\":0,\"eact\":-1,\"etime_opt\":-1,\"emin\":0}}}")]
    [DataRow("schedule", "delete_rule", "{\"schedule\":{\"delete_rule\":{\"id\":\"0123456789ABCDEF0123456789ABCDEF\"}}}")]
    [DataRow("schedule", "get_daystat", "{\"schedule\":{\"get_daystat\":{\"year\":2026,\"month\":6}}}")]
    [DataRow("schedule", "get_monthstat", "{\"schedule\":{\"get_monthstat\":{\"year\":2026}}}")]
    [DataRow("anti_theft", "add_rule", "{\"anti_theft\":{\"add_rule\":{\"name\":\"HVO Lab\",\"enable\":1,\"wday\":[0,0,0,0,0,0,1],\"repeat\":0,\"sact\":1,\"stime_opt\":0,\"smin\":720,\"soffset\":0,\"eact\":-1,\"etime_opt\":-1,\"emin\":0}}}")]
    [DataRow("anti_theft", "delete_rule", "{\"anti_theft\":{\"delete_rule\":{\"id\":\"0123456789ABCDEF0123456789ABCDEF\"}}}")]
    [DataRow("count_down", "add_rule", "{\"count_down\":{\"add_rule\":{\"name\":\"HVO Lab\",\"enable\":1,\"delay\":60,\"act\":0}}}")]
    [DataRow("count_down", "add_rule", "{\"count_down\":{\"add_rule\":{\"name\":\"Timer AddTimerObject\",\"enable\":1,\"delay\":60,\"act\":1}}}")]
    [DataRow("count_down", "edit_rule", "{\"count_down\":{\"edit_rule\":{\"id\":\"0123456789ABCDEF0123456789ABCDEF\",\"name\":\"Timer AddTimerObject\",\"enable\":1,\"delay\":120,\"act\":1}}}")]
    [DataRow("count_down", "delete_rule", "{\"count_down\":{\"delete_rule\":{\"id\":\"0123456789ABCDEF0123456789ABCDEF\"}}}")]
    [DataRow("emeter", "erase_emeter_stat", "{\"emeter\":{\"erase_emeter_stat\":{}}}")]
    [DataRow("system", "get_dev_icon", "{\"system\":{\"get_dev_icon\":{}}}")]
    [DataRow("system", "get_download_state", "{\"system\":{\"get_download_state\":{}}}")]
    [DataRow("cnCloud", "get_info", "{\"cnCloud\":{\"get_info\":{}}}")]
    [DataRow("cnCloud", "get_intl_fw_list", "{\"cnCloud\":{\"get_intl_fw_list\":{}}}")]
    [DataRow("smartlife.iot.dimmer", "get_default_behavior", "{\"smartlife.iot.dimmer\":{\"get_default_behavior\":{}}}")]
    [DataRow("smartlife.iot.dimmer", "get_dimmer_parameters", "{\"smartlife.iot.dimmer\":{\"get_dimmer_parameters\":{}}}")]
    [DataRow("smartlife.iot.dimmer", "set_brightness", "{\"smartlife.iot.dimmer\":{\"set_brightness\":{\"brightness\":40}}}")]
    public async Task LabClient_AllowsGuardedDeskLampExerciseCommands(string module, string command, string commandJson)
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo(module, command, $"{{\"{module}\":{{\"{command}\":{{\"err_code\":0}}}}}}");
        var client = new KasaLegacyLabClient(TimeSpan.FromSeconds(2));

        using var response = await client.SendAsync("127.0.0.1", server.Port, commandJson, CancellationToken.None);

        response.RootElement.GetProperty(module).GetProperty(command).GetProperty("err_code").GetInt32()
            .Should().Be(0);
    }

    [TestMethod]
    [DataRow("{\"system\":{\"set_relay_state\":{\"state\":2}}}")]
    [DataRow("{\"system\":{\"reboot\":{\"delay\":0}}}")]
    [DataRow("{\"netif\":{\"set_stainfo\":{\"ssid\":\"x\",\"password\":\"y\"}}}")]
    [DataRow("{\"system\":{\"reset\":{}}}")]
    [DataRow("{\"system\":{\"set_mac_addr\":{\"mac\":\"00:11:22:33:44:55\"}}}")]
    [DataRow("{\"cnCloud\":{\"bind\":{\"username\":\"x\",\"password\":\"y\"}}}")]
    [DataRow("{\"schedule\":{\"delete_rule\":{\"id\":\"not-ours\"}}}")]
    [DataRow("{\"schedule\":{\"get_daystat\":{\"year\":2026,\"month\":13}}}")]
    [DataRow("{\"anti_theft\":{\"add_rule\":{\"name\":\"HVO Lab\"}}}")]
    [DataRow("{\"count_down\":{\"add_rule\":{\"name\":\"HVO Lab\",\"enable\":1,\"delay\":3600,\"act\":0}}}")]
    [DataRow("{\"count_down\":{\"edit_rule\":{\"id\":\"not-ours\",\"name\":\"Timer AddTimerObject\",\"enable\":1,\"delay\":120,\"act\":1}}}")]
    [DataRow("{\"count_down\":{\"edit_rule\":{\"id\":\"0123456789ABCDEF0123456789ABCDEF\",\"name\":\"HVO Lab\",\"enable\":1,\"delay\":120,\"act\":1}}}")]
    [DataRow("{\"smartlife.iot.dimmer\":{\"set_brightness\":{\"brightness\":0}}}")]
    [DataRow("{\"smartlife.iot.dimmer\":{\"set_brightness\":{\"brightness\":101}}}")]
    [DataRow("{\"smartlife.iot.dimmer\":{\"set_dimmer_transition\":{\"brightness\":40,\"duration\":1000}}}")]
    [DataRow("{\"smartlife.iot.dimmer\":{\"set_dimmer_parameters\":{\"minThreshold\":0}}}")]
    public async Task LabClient_RejectsNonRelayWriteCommands(string commandJson)
    {
        var client = new KasaLegacyLabClient(TimeSpan.FromSeconds(2));

        var act = async () => await client.SendAsync("127.0.0.1", 9999, commandJson, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
