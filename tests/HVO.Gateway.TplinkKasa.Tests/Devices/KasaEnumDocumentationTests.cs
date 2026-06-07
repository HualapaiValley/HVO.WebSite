using FluentAssertions;
using HVO.Gateway.TplinkKasa.Devices;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaEnumDocumentationTests
{
    [TestMethod]
    public void DeviceInventory_DocumentsAllHvoEnumValues()
    {
        var document = ReadInventoryDocument();

        AssertEnumValuesDocumented<KasaDeviceKind>(document);
        AssertEnumValuesDocumented<KasaCapability>(document);
        AssertEnumValuesDocumented<KasaMetadataCapability>(document);
        AssertEnumValuesDocumented<KasaCommandCapability>(document);
        AssertEnumValuesDocumented<KasaSafetyClass>(document);
    }

    [TestMethod]
    public void DeviceInventory_DocumentsKnownVendorModeValues()
    {
        var document = ReadInventoryDocument();
        var knownModeValues = new[]
        {
            "normal",
            "circadian",
            "last_status",
            "customize_preset",
            "gentle_on_off",
            "instant_on_off",
            "gentle_on",
            "none",
            "unknown"
        };

        foreach (var value in knownModeValues)
        {
            document.Should().Contain($"`{value}`");
        }
    }

    [TestMethod]
    public void DeviceInventory_DocumentsKnownVendorParameterValueDomains()
    {
        var document = ReadInventoryDocument();
        var knownParameterTokens = new[]
        {
            "`wday[0..6]`",
            "Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday",
            "`repeat`",
            "`0` one-time",
            "`1` repeating",
            "`sact`",
            "`eact`",
            "`0` off",
            "`1` on",
            "`-1` no end action",
            "`stime_opt`",
            "`etime_opt`",
            "`0` fixed minute-of-day",
            "`smin`",
            "`emin`",
            "`0-1439` minute of day",
            "`get_next_action.type`",
            "`1` scheduled rule",
            "`2` active countdown/app timer",
            "`count_down.*.act`",
            "`delay`",
            "`remain`",
            "`5-300` seconds",
            "`system.get_sysinfo.active_mode`",
            "`none`",
            "`schedule`",
            "`count_down`",
            "`set_led_off.off`",
            "`0-200`",
            "`1-100`",
            "`0-100`",
            "`0-360`",
            "`2500-9000 K`",
            "`0-3`"
        };

        foreach (var token in knownParameterTokens)
        {
            document.Should().Contain(token);
        }
    }

    private static void AssertEnumValuesDocumented<TEnum>(string document)
        where TEnum : struct, Enum
    {
        foreach (var name in Enum.GetNames<TEnum>())
        {
            document.Should().Contain($"`{name}`");
        }
    }

    private static string ReadInventoryDocument()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "gateways", "tplink-kasa", "device-api-inventory.md");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate docs/gateways/tplink-kasa/device-api-inventory.md from the test output directory.");
    }
}
