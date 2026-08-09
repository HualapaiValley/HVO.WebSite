using HVO.Edge.Contracts;

namespace HVO.Edge.Hosting.Telemetry;

public static class GatewayTelemetryResource
{
    public static IReadOnlyList<KeyValuePair<string, object>> Create(
        string defaultServiceName,
        GatewayTelemetryIdentity identity,
        string environmentName,
        string? serviceInstanceId = null)
    {
        var attributes = new List<KeyValuePair<string, object>>
        {
            new(GatewayTelemetryConventions.ResourceAttributes.ServiceName,
                Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? defaultServiceName),
            new(GatewayTelemetryConventions.ResourceAttributes.GatewayId, identity.GatewayId),
            new(GatewayTelemetryConventions.ResourceAttributes.GatewayType, identity.GatewayType),
            new(GatewayTelemetryConventions.ResourceAttributes.DeploymentEnvironment, environmentName),
            new(GatewayTelemetryConventions.ResourceAttributes.ServiceInstanceId,
                serviceInstanceId ?? identity.GatewayId),
            new(GatewayTelemetryConventions.ResourceAttributes.ServiceVersion,
                GatewayTelemetryConventions.Version)
        };

        if (!string.IsNullOrWhiteSpace(identity.SiteId))
            attributes.Add(new(GatewayTelemetryConventions.ResourceAttributes.SiteId, identity.SiteId));

        return attributes;
    }
}
