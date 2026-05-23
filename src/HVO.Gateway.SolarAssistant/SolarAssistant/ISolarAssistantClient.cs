namespace HVO.Gateway.SolarAssistant.SolarAssistant;

public interface ISolarAssistantClient
{
    Task<IReadOnlyList<SolarAssistantMetric>> GetMetricsAsync(CancellationToken ct);
}
