namespace HVO.Hardware.VictronSmartShunt.SmartShunt;

public static class SmartShuntPublicProtocol
{
    public const string PublicKeepAliveUuid = "6597ffff-4bda-4c1e-af4b-551c4cf74769";
    public static readonly byte[] PublicKeepAlivePayload = [0x20, 0x4e];

    public static readonly IReadOnlyList<SmartShuntPublicField> Fields =
    [
        new("soc", "65970fff-4bda-4c1e-af4b-551c4cf74769", DecodeUnsignedHundredths, "ffff"),
        new("voltage", "6597ed8d-4bda-4c1e-af4b-551c4cf74769", DecodeSignedHundredths, "ff7f"),
        new("power", "6597ed8e-4bda-4c1e-af4b-551c4cf74769", DecodeSignedInt16, "ff7f"),
        new("current", "6597ed8c-4bda-4c1e-af4b-551c4cf74769", DecodeSignedThousandths, "ffffff7f"),
        new("consumed_ah", "6597eeff-4bda-4c1e-af4b-551c4cf74769", DecodeSignedTenths, "ffffff7f"),
        new("starter_voltage", "6597ed7d-4bda-4c1e-af4b-551c4cf74769", DecodeSignedHundredths, "ff7f"),
        new("val2", "6597edec-4bda-4c1e-af4b-551c4cf74769", DecodeUnsignedInt16, "ffff"),
        new("val3", "65970382-4bda-4c1e-af4b-551c4cf74769", DecodeUnsignedInt16, "ffff"),
        new("temperature", "65970383-4bda-4c1e-af4b-551c4cf74769", DecodeSignedInt16, "ff7f"),
        new("remaining_time", "65970ffe-4bda-4c1e-af4b-551c4cf74769", DecodeUnsignedInt16, "ffff"),
    ];

    public static SmartShuntLiveSample DecodeSample(IReadOnlyDictionary<string, byte[]> values, DateTime recordedAtUtc)
    {
        double? ReadValue(string key)
        {
            var field = Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
            if (field is null)
                return null;

            if (!values.TryGetValue(field.Key, out var payload))
                return null;

            return field.Decode(payload, field.NotAvailableHex);
        }

        return new SmartShuntLiveSample
        {
            RecordedAtUtc = recordedAtUtc,
            StateOfChargePercent = ReadValue("soc"),
            VoltageV = ReadValue("voltage"),
            CurrentA = ReadValue("current"),
            PowerW = ReadValue("power"),
            ConsumedAh = ReadValue("consumed_ah"),
            StarterVoltageV = ReadValue("starter_voltage"),
            TemperatureC = ReadValue("temperature"),
            RemainingMinutes = ReadValue("remaining_time"),
            PublicSessionActive = true,
        };
    }

    private static double? DecodeUnsignedHundredths(byte[] value, string notAvailableHex)
        => MatchesNotAvailable(value, notAvailableHex) ? null : BitConverter.ToUInt16(value, 0) / 100.0;

    private static double? DecodeSignedHundredths(byte[] value, string notAvailableHex)
        => MatchesNotAvailable(value, notAvailableHex) ? null : BitConverter.ToInt16(value, 0) / 100.0;

    private static double? DecodeSignedTenths(byte[] value, string notAvailableHex)
        => MatchesNotAvailable(value, notAvailableHex) ? null : BitConverter.ToInt32(value, 0) / 10.0;

    private static double? DecodeSignedThousandths(byte[] value, string notAvailableHex)
        => MatchesNotAvailable(value, notAvailableHex) ? null : BitConverter.ToInt32(value, 0) / 1000.0;

    private static double? DecodeSignedInt16(byte[] value, string notAvailableHex)
        => MatchesNotAvailable(value, notAvailableHex) ? null : BitConverter.ToInt16(value, 0);

    private static double? DecodeUnsignedInt16(byte[] value, string notAvailableHex)
        => MatchesNotAvailable(value, notAvailableHex) ? null : BitConverter.ToUInt16(value, 0);

    private static bool MatchesNotAvailable(byte[] value, string notAvailableHex)
        => string.Equals(Convert.ToHexString(value), notAvailableHex, StringComparison.OrdinalIgnoreCase);
}

public sealed record SmartShuntPublicField(
    string Key,
    string Uuid,
    Func<byte[], string, double?> Decode,
    string NotAvailableHex);
