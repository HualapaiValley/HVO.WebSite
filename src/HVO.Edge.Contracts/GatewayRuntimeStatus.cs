namespace HVO.Edge.Contracts;

public sealed record GatewayRuntimeStatus(
    GatewayIdentity Identity,
    DateTime? ObservedAtUtc,
    GatewaySampleState SampleState,
    IReadOnlySet<GatewayCapability> Capabilities,
    string? DataPath = null,
    string? LastError = null);
