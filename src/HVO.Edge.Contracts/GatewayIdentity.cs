namespace HVO.Edge.Contracts;

public sealed record GatewayIdentity(
    string GatewayId,
    string DisplayName,
    GatewayDomain Domain,
    string SourceId,
    string? DeviceId = null,
    string? RuntimeHost = null);
