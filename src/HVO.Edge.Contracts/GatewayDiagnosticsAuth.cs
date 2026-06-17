using Microsoft.AspNetCore.Http;

namespace HVO.Edge.Contracts;

public static class GatewayDiagnosticsAuth
{
    public static bool HasMatchingApiKey(HttpContext httpContext, string? configuredApiKey)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Request.Headers.TryGetValue(GatewayApiKeyMatcher.HeaderName, out var providedApiKey)
            && providedApiKey.Count > 0
            && GatewayApiKeyMatcher.IsMatch(configuredApiKey, providedApiKey[0]);
    }
}
