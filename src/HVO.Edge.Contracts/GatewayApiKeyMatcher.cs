namespace HVO.Edge.Contracts;

public static class GatewayApiKeyMatcher
{
    public const string HeaderName = "X-Api-Key";

    public static bool IsMatch(string? configuredApiKey, string? providedApiKey)
    {
        if (!IsConfiguredApiKeyUsable(configuredApiKey))
        {
            return false;
        }

        return !string.IsNullOrEmpty(providedApiKey)
            && string.Equals(providedApiKey, configuredApiKey, StringComparison.Ordinal);
    }

    private static bool IsConfiguredApiKeyUsable(string? configuredApiKey) =>
        !string.IsNullOrWhiteSpace(configuredApiKey)
        && !string.Equals(configuredApiKey, "REPLACE_ME", StringComparison.OrdinalIgnoreCase)
        && !configuredApiKey.Contains("__SET_", StringComparison.Ordinal);
}
