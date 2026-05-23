using HVO.Ingest.Contracts.Weather;

namespace HVO.Hardware.DavisVantagePro2.Ingest;

public interface IWeatherRawPublisher
{
    Task PublishAsync(WeatherRawV1 payload, CancellationToken ct);
}
