using System.ComponentModel.DataAnnotations;

namespace HVO.Hardware.Eg4.Configuration;

public sealed class Eg4Options
{
    public const string SectionName = "Eg4";

    [Range(10, 3600)]
    public int DefaultPollIntervalSeconds { get; set; } = 15;

    public bool SimulationEnabled { get; set; }
    public string? CentralIngestEndpoint { get; set; }
    public string CentralApiKeySecret { get; set; } = "central-ingest-api-key";
    public bool AllowInsecureCentralIngest { get; set; }
    [Range(1, 1440)]
    public int RetryExhaustedRequeueMinutes { get; set; } = 15;
    public List<Eg4DeviceOptions> Devices { get; set; } = [];
}

public sealed class Eg4DeviceOptions
{
    public Eg4DeviceType Type { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Port { get; set; } = string.Empty;
    public byte UnitId { get; set; }
    public int? PollIntervalSeconds { get; set; }
}

public enum Eg4DeviceType
{
    Unknown = 0,
    Inverter6500Ex = 1,
    ChargeControllerMppt10048Hv = 2,
}
