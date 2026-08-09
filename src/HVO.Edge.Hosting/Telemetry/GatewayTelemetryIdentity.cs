namespace HVO.Edge.Hosting.Telemetry;

public sealed record GatewayTelemetryIdentity(
    string GatewayId,
    string GatewayType,
    string? SiteId = null);
