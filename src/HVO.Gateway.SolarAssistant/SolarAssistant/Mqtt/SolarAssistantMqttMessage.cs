namespace HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;

public sealed class SolarAssistantMqttMessage
{
    public string Topic { get; init; } = string.Empty;

    public string Payload { get; init; } = string.Empty;

    public bool Retain { get; init; }

    public int Qos { get; init; }

    public DateTime ReceivedAtUtc { get; init; } = DateTime.UtcNow;
}
