using FluentAssertions;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.Tests.Live;

[TestClass]
[TestCategory("Live")]
public sealed class SmartShuntLiveTests
{
    [TestMethod]
    public async Task DirectPublicGatt_ProducesRequiredFieldsWithinBoundedWindow()
    {
        var address = Environment.GetEnvironmentVariable("SMARTSHUNT_LIVE_ADDRESS");
        if (string.IsNullOrWhiteSpace(address)) Assert.Inconclusive("Set SMARTSHUNT_LIVE_ADDRESS for the bounded live validation.");
        var session = new SmartShuntPublicSession(Options.Create(new SmartShuntOptions { Address = address, Adapter = Environment.GetEnvironmentVariable("SMARTSHUNT_LIVE_ADAPTER") ?? "hci0" }), NullLogger<SmartShuntPublicSession>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await session.StartAsync(cts.Token);
        try
        {
            while (!cts.IsCancellationRequested && session.CurrentSample is null) await Task.Delay(500, cts.Token);
            session.CurrentSample.Should().NotBeNull();
            var sample = session.CurrentSample!;
            sample.VoltageV.Should().NotBeNull(); sample.CurrentA.Should().NotBeNull(); sample.PowerW.Should().NotBeNull(); sample.StateOfChargePercent.Should().NotBeNull();
        }
        finally { await session.StopAsync(CancellationToken.None); session.Dispose(); }
    }
}
