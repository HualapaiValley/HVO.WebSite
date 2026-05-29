using System.Globalization;
using System.Text.RegularExpressions;

namespace HVO.Gateway.TplinkKasa.Devices;

public static partial class KasaJson
{
    public static string? NormalizeMacAddress(string? macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress))
        {
            return null;
        }

        var hex = NonHexRegex().Replace(macAddress, string.Empty).ToUpperInvariant();
        return hex.Length == 12 ? hex : macAddress.Trim().ToUpperInvariant();
    }

    public static bool MacAddressesEqual(string? left, string? right)
    {
        var normalizedLeft = NormalizeMacAddress(left);
        var normalizedRight = NormalizeMacAddress(right);
        return normalizedLeft is not null
            && normalizedRight is not null
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetInt(System.Text.Json.JsonElement element, out int value)
    {
        value = default;
        if (element.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            return element.TryGetInt32(out value);
        }

        return element.ValueKind == System.Text.Json.JsonValueKind.String
            && int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryGetDouble(System.Text.Json.JsonElement element, out double value)
    {
        value = default;
        if (element.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            return element.TryGetDouble(out value);
        }

        return element.ValueKind == System.Text.Json.JsonValueKind.String
            && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    [GeneratedRegex("[^0-9A-Fa-f]")]
    private static partial Regex NonHexRegex();
}
