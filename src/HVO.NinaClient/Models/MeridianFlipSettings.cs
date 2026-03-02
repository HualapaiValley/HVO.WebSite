using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Meridian flip settings from NINA profile
/// </summary>
public record MeridianFlipSettings
{
    [JsonPropertyName("AutoFocusAfterFlip")]
    public bool AutoFocusAfterFlip { get; init; }

    [JsonPropertyName("Enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("MaxMinutesAfterMeridian")]
    public double MaxMinutesAfterMeridian { get; init; }

    [JsonPropertyName("MinutesAfterMeridian")]
    public double MinutesAfterMeridian { get; init; }

    [JsonPropertyName("PauseTimeoutMinutes")]
    public double PauseTimeoutMinutes { get; init; }

    [JsonPropertyName("Recenter")]
    public bool Recenter { get; init; }

    [JsonPropertyName("RotateGuider")]
    public bool RotateGuider { get; init; }

    [JsonPropertyName("SettleTime")]
    public double SettleTime { get; init; }

    [JsonPropertyName("UseSideOfPier")]
    public bool UseSideOfPier { get; init; }
}
