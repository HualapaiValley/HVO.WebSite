using HVO.Edge.Contracts;
using Microsoft.AspNetCore.Http;

namespace HVO.Hardware.VictronSmartShunt.SmartShunt.Health;

public static class SmartShuntGatewayDiagnosticsAuth
{
    public static bool HasMatchingApiKey(HttpContext httpContext, string configuredApiKey)
    {
        return httpContext.Request.Headers.TryGetValue(GatewayApiKeyMatcher.HeaderName, out var providedApiKey)
            && providedApiKey.Count > 0
            && GatewayApiKeyMatcher.IsMatch(configuredApiKey, providedApiKey[0]);
    }
}
