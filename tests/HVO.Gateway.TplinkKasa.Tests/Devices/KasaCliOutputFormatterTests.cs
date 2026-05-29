using FluentAssertions;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Protocol;
using HVO.Gateway.TplinkKasa.Tests.Fakes;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaCliOutputFormatterTests
{
    [TestMethod]
    public async Task FormatProbeResult_DefaultOutputDoesNotEmitRawOrHashedIdentifiers()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("ep25-sysinfo.json"));
        var probe = new KasaReadOnlyProbe(
            new KasaLegacyClient(TimeSpan.FromSeconds(2)),
            new KasaSystemInfoParser(),
            new KasaEnergyParser(),
            new KasaCapabilityDetector());

        var result = await probe.ProbeAsync("127.0.0.1", server.Port, CancellationToken.None);
        var output = KasaCliOutputFormatter.FormatProbeResult(
            result,
            includeIdentifiers: false,
            includeLocators: false,
            includeShapes: false);

        output.Should().NotContain("127.0.0.1");
        output.Should().NotContain("EP25_DEVICE_ID_SANITIZED");
        output.Should().NotContain("AA:BB:CC:DD:EE:01");
        output.Should().NotContain("HostHash");
        output.Should().NotContain("DeviceIdHash");
        output.Should().NotContain("MacAddressHash");
        output.Should().Contain("HostPresent");
        output.Should().Contain("DeviceIdPresent");
        output.Should().Contain("MacAddressPresent");
    }
}
