namespace HVO.Edge.Contracts;

public sealed record GatewayHealthAlert(
    string Code,
    GatewayAlertSeverity Severity,
    string Message);
