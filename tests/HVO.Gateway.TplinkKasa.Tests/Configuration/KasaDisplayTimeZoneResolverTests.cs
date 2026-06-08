using FluentAssertions;
using HVO.Gateway.TplinkKasa.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.TplinkKasa.Tests.Configuration;

[TestClass]
public sealed class KasaDisplayTimeZoneResolverTests
{
    [TestMethod]
    public void ConvertFromUtc_UsesGatewayDisplayTimeZone()
    {
        var resolver = new KasaDisplayTimeZoneResolver(Options.Create(new KasaGatewayOptions
        {
            DisplayTimeZoneId = "America/Phoenix"
        }));
        var utc = new DateTimeOffset(2026, 6, 6, 18, 0, 0, TimeSpan.Zero);

        var local = resolver.ConvertFromUtc(utc);

        local.Offset.Should().Be(TimeSpan.FromHours(-7));
        local.Hour.Should().Be(11);
    }

    [TestMethod]
    public void ConvertFromUtc_UsesDeviceOverrideWhenConfigured()
    {
        var resolver = new KasaDisplayTimeZoneResolver(Options.Create(new KasaGatewayOptions
        {
            DisplayTimeZoneId = "America/Phoenix"
        }));
        var utc = new DateTimeOffset(2026, 6, 6, 18, 0, 0, TimeSpan.Zero);

        var local = resolver.ConvertFromUtc(utc, "America/New_York");

        local.Offset.Should().Be(TimeSpan.FromHours(-4));
        local.Hour.Should().Be(14);
    }

    [TestMethod]
    public void ConvertFromUtc_InvalidTimeZoneFallsBackToUtc()
    {
        var resolver = new KasaDisplayTimeZoneResolver(Options.Create(new KasaGatewayOptions
        {
            DisplayTimeZoneId = "Not/AZone"
        }));
        var utc = new DateTimeOffset(2026, 6, 6, 18, 0, 0, TimeSpan.Zero);

        var local = resolver.ConvertFromUtc(utc);

        local.Offset.Should().Be(TimeSpan.Zero);
        local.Hour.Should().Be(18);
    }
}