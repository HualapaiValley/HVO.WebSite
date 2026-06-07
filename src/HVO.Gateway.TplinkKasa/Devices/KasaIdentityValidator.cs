using HVO.Gateway.TplinkKasa.Configuration;

namespace HVO.Gateway.TplinkKasa.Devices;

public sealed class KasaIdentityValidator
{
    public KasaIdentityValidationResult Validate(KasaDeviceConfig config, KasaSystemInfo info)
    {
        if (string.IsNullOrWhiteSpace(config.DeviceId))
        {
            return Invalid("Configured DeviceId is required.", false, false, false, false);
        }

        var deviceIdMatched = string.Equals(config.DeviceId, info.DeviceId, StringComparison.OrdinalIgnoreCase);
        var allowLegacyScanIdentity = IsLegacyScanIdentity(config)
            && !string.IsNullOrWhiteSpace(config.MacAddress)
            && !string.IsNullOrWhiteSpace(info.MacAddress)
            && KasaJson.MacAddressesEqual(config.MacAddress, info.MacAddress);
        if (!deviceIdMatched && !allowLegacyScanIdentity)
        {
            return Invalid("Connected deviceId did not match configured DeviceId.", false, false, false, false);
        }

        var macMatched = true;
        if (!string.IsNullOrWhiteSpace(config.MacAddress) && !string.IsNullOrWhiteSpace(info.MacAddress))
        {
            macMatched = KasaJson.MacAddressesEqual(config.MacAddress, info.MacAddress);
            if (!macMatched)
            {
                return Invalid("Connected device MAC did not match configured MAC.", true, false, false, false);
            }
        }

        var modelMatched = true;
        if (!string.IsNullOrWhiteSpace(config.ExpectedModel) && !string.IsNullOrWhiteSpace(info.Model))
        {
            modelMatched = string.Equals(config.ExpectedModel, info.Model, StringComparison.OrdinalIgnoreCase);
            if (!modelMatched)
            {
                return Invalid("Connected device model did not match configured model.", true, macMatched, false, false);
            }
        }

        var childCountMatched = true;
        if (config.ExpectedChildCount is int expectedChildCount)
        {
            childCountMatched = expectedChildCount == info.Children.Count;
            if (!childCountMatched)
            {
                return Invalid("Connected device child count did not match configured child count.", true, macMatched, modelMatched, false);
            }
        }

        return new KasaIdentityValidationResult(true, null, deviceIdMatched, macMatched, modelMatched, childCountMatched);
    }

    private static bool IsLegacyScanIdentity(KasaDeviceConfig config) =>
        config.DeviceId.StartsWith("kasa-", StringComparison.OrdinalIgnoreCase)
        && string.Equals(config.DeviceId, config.SourceId?.Replace("tplink-kasa:", string.Empty, StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase);

    private static KasaIdentityValidationResult Invalid(string reason, bool deviceIdMatched, bool macMatched, bool modelMatched, bool childCountMatched) =>
        new(false, reason, deviceIdMatched, macMatched, modelMatched, childCountMatched);
}
