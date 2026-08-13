namespace HVO.Edge.Hosting.Logging;

public sealed record GatewayLogIdentity(
    string DefaultServiceName,
    string GatewayId,
    string GatewayType,
    string? SourceId = null,
    string? DeviceId = null,
    string? SiteId = null,
    string? ServiceVersion = null,
    string? ServiceInstanceId = null,
    string? HostName = null);
