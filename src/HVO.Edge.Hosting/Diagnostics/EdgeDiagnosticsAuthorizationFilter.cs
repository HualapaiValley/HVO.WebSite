using HVO.Edge.Contracts;
using Microsoft.AspNetCore.Http;

namespace HVO.Edge.Hosting.Diagnostics;

internal sealed class EdgeDiagnosticsAuthorizationFilter(EdgeDiagnosticsCredential credential) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        return GatewayDiagnosticsAuth.HasMatchingApiKey(context.HttpContext, credential.ApiKey)
            ? await next(context).ConfigureAwait(false)
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }
}
