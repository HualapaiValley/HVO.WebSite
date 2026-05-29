using FluentAssertions;
using HVO.Gateway.TplinkKasa.Devices;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaReadMetadataParserTests
{
    [TestMethod]
    public void ParseDimmerDefaultBehavior_ExtractsSafeModeFields()
    {
        using var response = JsonDocument.Parse("""
        {
          "smartlife.iot.dimmer": {
            "get_default_behavior": {
              "err_code": 0,
              "soft_on": { "mode": "last_status" },
              "hard_on": { "mode": "customize_preset" },
              "double_click": { "mode": "gentle_on" },
              "long_press": { "mode": "instant_on_off" }
            }
          }
        }
        """);
        var parser = new KasaReadMetadataParser();

        var metadata = parser.ParseDimmerDefaultBehavior(response);

        metadata.IsSupported.Should().BeTrue();
        metadata.SoftOnMode.Should().Be("last_status");
        metadata.HardOnMode.Should().Be("customize_preset");
        metadata.DoubleClickMode.Should().Be("gentle_on");
        metadata.LongPressMode.Should().Be("instant_on_off");
    }

    [TestMethod]
    public void ParseDimmerParameters_ExtractsSafeParameterFields()
    {
        using var response = JsonDocument.Parse("""
        {
          "smartlife.iot.dimmer": {
            "get_dimmer_parameters": {
              "err_code": 0,
              "bulb_type": 1,
              "fadeOnTime": 1000,
              "fadeOffTime": 2000,
              "gentleOnTime": 3000,
              "gentleOffTime": 4000,
              "minThreshold": 11,
              "rampRate": 22
            }
          }
        }
        """);
        var parser = new KasaReadMetadataParser();

        var metadata = parser.ParseDimmerParameters(response);

        metadata.IsSupported.Should().BeTrue();
        metadata.BulbType.Should().Be(1);
        metadata.FadeOnTimeMs.Should().Be(1000);
        metadata.FadeOffTimeMs.Should().Be(2000);
        metadata.GentleOnTimeMs.Should().Be(3000);
        metadata.GentleOffTimeMs.Should().Be(4000);
        metadata.MinThreshold.Should().Be(11);
        metadata.RampRate.Should().Be(22);
    }
}
