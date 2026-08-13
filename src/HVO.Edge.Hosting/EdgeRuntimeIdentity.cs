using System.Reflection;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Logging;
using HVO.Edge.Hosting.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HVO.Edge.Hosting;

public sealed record EdgeRuntimeIdentity(
    string ServiceName,
    string ServiceVersion,
    string ServiceInstanceId,
    string GatewayId,
    string GatewayType,
    GatewayDomain Domain,
    string SourceId,
    string? SiteId,
    string? DeviceId,
    string EnvironmentName,
    string HostName,
    string DisplayName)
{
    internal static EdgeRuntimeIdentity Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(EdgeRuntimeOptions.SectionName).Get<EdgeRuntimeOptions>() ?? new();
        var gatewayId = Required(options.GatewayId, "Edge:Runtime:GatewayId");
        var gatewayType = Required(options.GatewayType, "Edge:Runtime:GatewayType");
        var serviceName = configuration["OTEL_SERVICE_NAME"]
            ?? Required(options.ServiceName, "Edge:Runtime:ServiceName");
        var hostName = configuration["HOSTNAME"] ?? Environment.MachineName;

        return new EdgeRuntimeIdentity(
            serviceName,
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? GatewayTelemetryConventions.Version,
            configuration["OTEL_SERVICE_INSTANCE_ID"] ?? options.ServiceInstanceId ?? hostName,
            gatewayId,
            gatewayType,
            options.Domain,
            Required(options.SourceId ?? gatewayId, "Edge:Runtime:SourceId"),
            Normalize(options.SiteId),
            Normalize(options.DeviceId),
            environment.EnvironmentName,
            hostName,
            Normalize(options.DisplayName) ?? gatewayId);
    }

    public IReadOnlyList<KeyValuePair<string, object>> CreateResourceAttributes()
    {
        var attributes = new List<KeyValuePair<string, object>>
        {
            new(GatewayTelemetryConventions.ResourceAttributes.ServiceName, ServiceName),
            new(GatewayTelemetryConventions.ResourceAttributes.ServiceVersion, ServiceVersion),
            new(GatewayTelemetryConventions.ResourceAttributes.ServiceInstanceId, ServiceInstanceId),
            new(GatewayTelemetryConventions.ResourceAttributes.DeploymentEnvironment, EnvironmentName),
            new(GatewayTelemetryConventions.ResourceAttributes.GatewayId, GatewayId),
            new(GatewayTelemetryConventions.ResourceAttributes.GatewayType, GatewayType),
            new("host.name", HostName),
            new(GatewayTelemetryConventions.Tags.SourceId, SourceId)
        };

        if (SiteId is not null)
            attributes.Add(new(GatewayTelemetryConventions.ResourceAttributes.SiteId, SiteId));
        if (DeviceId is not null)
            attributes.Add(new(GatewayTelemetryConventions.Tags.DeviceId, DeviceId));
        return attributes;
    }

    internal GatewayLogIdentity ToLogIdentity() =>
        new(ServiceName, GatewayId, GatewayType, SourceId, DeviceId, SiteId, ServiceVersion, ServiceInstanceId, HostName);

    internal GatewayTelemetryIdentity ToTelemetryIdentity() =>
        new(GatewayId, GatewayType, SiteId);

    internal GatewayIdentity ToGatewayIdentity() =>
        new(GatewayId, DisplayName, Domain, SourceId, DeviceId, HostName);

    private static string Required(string? value, string key) =>
        Normalize(value) ?? throw new InvalidOperationException($"{key} is required.");

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
