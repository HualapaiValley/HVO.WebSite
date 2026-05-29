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
}
