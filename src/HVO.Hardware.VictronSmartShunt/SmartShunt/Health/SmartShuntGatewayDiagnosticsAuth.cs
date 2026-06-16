using Microsoft.AspNetCore.Http;

namespace HVO.Hardware.VictronSmartShunt.SmartShunt.Health;

public static class SmartShuntGatewayDiagnosticsAuth
{
    public static bool HasMatchingApiKey(HttpContext httpContext, string configuredApiKey)
    {
        if (!IsConfiguredApiKeyUsable(configuredApiKey))
        {
            return false;
        }

        return httpContext.Request.Headers.TryGetValue("X-Api-Key", out var providedApiKey)
            && providedApiKey.Count > 0
            && string.Equals(providedApiKey[0], configuredApiKey, StringComparison.Ordinal);
    }

    private static bool IsConfiguredApiKeyUsable(string configuredApiKey) =>
        !string.IsNullOrWhiteSpace(configuredApiKey)
        && !string.Equals(configuredApiKey, "REPLACE_ME", StringComparison.OrdinalIgnoreCase)
        && !configuredApiKey.Contains("__SET_", StringComparison.Ordinal);
}
