using HVO.Edge.Contracts;
using Microsoft.AspNetCore.Http;

namespace HVO.Gateway.TplinkKasa.Hosting;

public static class KasaGatewayDiagnosticsAuth
{
    public static bool HasMatchingApiKey(HttpContext httpContext, string configuredApiKey)
    {
        return httpContext.Request.Headers.TryGetValue(GatewayApiKeyMatcher.HeaderName, out var providedApiKey)
            && providedApiKey.Count > 0
            && GatewayApiKeyMatcher.IsMatch(configuredApiKey, providedApiKey[0]);
    }
}
