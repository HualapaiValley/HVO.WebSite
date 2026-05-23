namespace HVO.Gateway.SolarAssistant.SolarAssistant.Health;

public enum SolarAssistantGatewayHealthSeverity
{
    Info,
    Warning,
    Critical,
}

public sealed class SolarAssistantGatewayHealthAlert
{
    public string Code { get; init; } = string.Empty;

    public SolarAssistantGatewayHealthSeverity Severity { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed class SolarAssistantGatewayHealthSnapshot
{
    public DateTime EvaluatedAtUtc { get; init; }

    public string State { get; init; } = "unknown";

    public IReadOnlyList<SolarAssistantGatewayHealthAlert> Alerts { get; init; } = [];
}
