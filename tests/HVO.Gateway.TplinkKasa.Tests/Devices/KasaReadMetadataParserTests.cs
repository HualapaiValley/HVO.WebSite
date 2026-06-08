using FluentAssertions;
using HVO.Gateway.TplinkKasa.Devices;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaReadMetadataParserTests
{
    [TestMethod]
    public void SystemInfoParser_ExtractsSafeDeviceInfoFields()
    {
        using var response = JsonDocument.Parse("""
        {
          "system": {
            "get_sysinfo": {
              "deviceId": "DEVICE_ID_SANITIZED",
              "alias": "Desk Lamp",
              "model": "EP25(US)",
              "type": "IOT.SMARTPLUGSWITCH",
              "hw_ver": "2.0",
              "sw_ver": "1.0.0",
              "mac": "AA:BB:CC:DD:EE:01",
              "hwId": "hardware-id",
              "fwId": "firmware-id",
              "oemId": "oem-id",
              "feature": "TIM:ENE",
              "active_mode": "schedule",
              "rssi": -53,
              "latitude_i": 351980,
              "longitude_i": -1140530,
              "relay_state": 1
            }
          }
        }
        """);
        var parser = new KasaSystemInfoParser();

        var info = parser.Parse(response);

        info.DeviceType.Should().Be("IOT.SMARTPLUGSWITCH");
        info.HardwareId.Should().Be("hardware-id");
        info.FirmwareId.Should().Be("firmware-id");
        info.OemId.Should().Be("oem-id");
        info.Feature.Should().Be("TIM:ENE");
        info.ActiveMode.Should().Be("schedule");
        info.Rssi.Should().Be(-53);
        info.Location.Should().NotBeNull();
        info.Location!.LatitudeDegrees.Should().Be(35.198);
        info.Location.LongitudeDegrees.Should().Be(-114.053);
    }

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
    public void ParseBulbDefaultBehavior_ExtractsSafeModeFields()
    {
        using var response = JsonDocument.Parse("""
        {
          "smartlife.iot.smartbulb.lightingservice": {
            "get_default_behavior": {
              "err_code": 0,
              "soft_on": { "mode": "last_status" },
              "hard_on": { "mode": "customize_preset" }
            }
          }
        }
        """);
        var parser = new KasaReadMetadataParser();

        var metadata = parser.ParseBulbDefaultBehavior(response);

        metadata.IsSupported.Should().BeTrue();
        metadata.SoftOnMode.Should().Be("last_status");
        metadata.HardOnMode.Should().Be("customize_preset");
    }

    [TestMethod]
    [DataRow("normal")]
    [DataRow("circadian")]
    public void SystemInfoParser_PreservesKnownBulbLightModes(string mode)
    {
        using var response = JsonDocument.Parse($$"""
        {
          "system": {
            "get_sysinfo": {
              "deviceId": "DEVICE_ID_SANITIZED",
              "alias": "Kitchen Sink Light",
              "model": "LB230(E26)",
              "type": "IOT.SMARTBULB",
              "hw_ver": "1.0",
              "sw_ver": "1.8.11",
              "mic_mac": "AA:BB:CC:DD:EE:08",
              "is_dimmable": 1,
              "is_color": 1,
              "is_variable_color_temp": 1,
              "light_state": {
                "on_off": 1,
                "mode": "{{mode}}",
                "hue": 0,
                "saturation": 0,
                "color_temp": 2700,
                "brightness": 5
              }
            }
          }
        }
        """);
        var parser = new KasaSystemInfoParser();

        var info = parser.Parse(response);

        info.LightState.Should().NotBeNull();
        info.LightState!.Mode.Should().Be(mode);
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
