using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Configuration options for NINA WebSocket client
/// </summary>
public record NinaWebSocketOptions
{
    /// <summary>
    /// The base URI for the NINA WebSocket server (default: ws://localhost:1888/v2)
    /// </summary>
    public string BaseUri { get; init; } = "ws://localhost:1888/v2";

    /// <summary>
    /// Connection timeout in milliseconds (default: 5000ms)
    /// </summary>
    public int ConnectionTimeoutMs { get; init; } = 5000;

    /// <summary>
    /// Keep-alive interval in milliseconds (default: 30000ms)
    /// </summary>
    public int KeepAliveIntervalMs { get; init; } = 30000;

    /// <summary>
    /// Buffer size for WebSocket messages (default: 4096 bytes)
    /// </summary>
    public int BufferSize { get; init; } = 4096;

    /// <summary>
    /// Maximum number of reconnection attempts (default: 5)
    /// </summary>
    public int MaxReconnectAttempts { get; init; } = 5;

    /// <summary>
    /// Delay between reconnection attempts in milliseconds (default: 2000ms)
    /// </summary>
    public int ReconnectDelayMs { get; init; } = 2000;
}

/// <summary>
/// NINA WebSocket event types
/// </summary>
public enum NinaEventType
{
    // API Events
    ApiCaptureFinished,
    
    // Autofocus Events
    AutofocusFinished,
    
    // Camera Events
    CameraConnected,
    CameraDisconnected,
    CameraDownloadTimeout,
    
    // Dome Events
    DomeConnected,
    DomeDisconnected,
    DomeShutterClosed,
    DomeShutterOpened,
    DomeHomed,
    DomeParked,
    DomeStopped,
    DomeSlewed,
    DomeSynced,
    
    // Filter Wheel Events
    FilterWheelConnected,
    FilterWheelDisconnected,
    FilterWheelChanged,
    
    // Flat Panel Events
    FlatConnected,
    FlatDisconnected,
    FlatLightToggled,
    FlatCoverOpened,
    FlatCoverClosed,
    FlatBrightnessChanged,
    
    // Focuser Events
    FocuserConnected,
    FocuserDisconnected,
    FocuserUserFocused,
    
    // Guider Events
    GuiderConnected,
    GuiderDisconnected,
    GuiderStart,
    GuiderStop,
    GuiderDither,
    
    // Mount Events
    MountConnected,
    MountDisconnected,
    MountBeforeFlip,
    MountAfterFlip,
    MountHomed,
    MountParked,
    MountUnparked,
    MountCenter,
    
    // Profile Events
    ProfileAdded,
    ProfileChanged,
    ProfileRemoved,
    
    // Rotator Events
    RotatorConnected,
    RotatorDisconnected,
    RotatorSynced,
    RotatorMoved,
    RotatorMovedMechanical,
    
    // Safety Events
    SafetyConnected,
    SafetyDisconnected,
    SafetyChanged,
    
    // Sequence Events
    SequenceStarting,
    SequenceFinished,
    SequenceEntityFailed,
    
    // Switch Events
    SwitchConnected,
    SwitchDisconnected,
    
    // Weather Events
    WeatherConnected,
    WeatherDisconnected,
    
    // Advanced Sequence Events
    AdvSeqStart,
    AdvSeqStop,
    
    // Error Events
    ErrorAf,
    ErrorPlatesolve,
    
    // Image Events
    ImageSave,
    
    // Stack Events
    StackUpdated,
    
    // Target Scheduler Events
    TsWaitStart,
    TsNewTargetStart,
    TsTargetStart,
    
    // General Events
    Unknown
}

/// <summary>
/// Base response for all NINA WebSocket messages
/// </summary>
public record NinaWebSocketResponse
{
    [JsonPropertyName("Response")]
    public object? Response { get; init; }

    [JsonPropertyName("Error")]
    public string? Error { get; init; }

    [JsonPropertyName("StatusCode")]
    public int StatusCode { get; init; }

    [JsonPropertyName("Success")]
    public bool Success { get; init; }

    [JsonPropertyName("Type")]
    public string? Type { get; init; }
}

/// <summary>
/// Simple event response with just an event name
/// </summary>
public record SimpleEventResponse
{
    [JsonPropertyName("Event")]
    public string? Event { get; init; }
}

/// <summary>
/// Filter change event response
/// </summary>
public record FilterChangedResponse
{
    [JsonPropertyName("Event")]
    public string? Event { get; init; }

    [JsonPropertyName("Previous")]
    public FilterInfo? Previous { get; init; }

    [JsonPropertyName("New")]
    public FilterInfo? New { get; init; }
}

/// <summary>
/// Flat brightness change event response
/// </summary>
public record FlatBrightnessChangedResponse
{
    [JsonPropertyName("Event")]
    public string? Event { get; init; }

    [JsonPropertyName("Previous")]
    public int Previous { get; init; }

    [JsonPropertyName("New")]
    public int New { get; init; }
}

/// <summary>
/// Safety change event response
/// </summary>
public record SafetyChangedResponse
{
    [JsonPropertyName("Event")]
    public string? Event { get; init; }

    [JsonPropertyName("IsSafe")]
    public bool IsSafe { get; init; }
}

/// <summary>
/// Stack update event response
/// </summary>
public record StackUpdatedResponse
{
    [JsonPropertyName("Event")]
    public string? Event { get; init; }

    [JsonPropertyName("Filter")]
    public string? Filter { get; init; }

    [JsonPropertyName("Target")]
    public string? Target { get; init; }
}

/// <summary>
/// Coordinates for target events
/// </summary>
public record Coordinates
{
    [JsonPropertyName("RA")]
    public double RA { get; init; }

    [JsonPropertyName("RAString")]
    public string? RAString { get; init; }

    [JsonPropertyName("RADegrees")]
    public double RADegrees { get; init; }

    [JsonPropertyName("Dec")]
    public double Dec { get; init; }

    [JsonPropertyName("DecString")]
    public string? DecString { get; init; }

    [JsonPropertyName("Epoch")]
    public string? Epoch { get; init; }

    [JsonPropertyName("DateTime")]
    public DateTimeInfo? DateTime { get; init; }
}

/// <summary>
/// Target start event response
/// </summary>
public record TargetStartResponse
{
    [JsonPropertyName("Event")]
    public string? Event { get; init; }

    [JsonPropertyName("TargetName")]
    public string? TargetName { get; init; }

    [JsonPropertyName("ProjectName")]
    public string? ProjectName { get; init; }

    [JsonPropertyName("Coordinates")]
    public Coordinates? Coordinates { get; init; }

    [JsonPropertyName("Rotation")]
    public double Rotation { get; init; }

    [JsonPropertyName("TargetEndTime")]
    public string? TargetEndTime { get; init; }
}

/// <summary>
/// Rotator move event response
/// </summary>
public record RotatorMovedResponse
{
    [JsonPropertyName("Event")]
    public string? Event { get; init; }

    [JsonPropertyName("From")]
    public double From { get; init; }

    [JsonPropertyName("To")]
    public double To { get; init; }
}

/// <summary>
/// Sequence entity failed event response
/// </summary>
public record SequenceEntityFailedResponse
{
    [JsonPropertyName("Event")]
    public string? Event { get; init; }

    [JsonPropertyName("Entity")]
    public string? Entity { get; init; }

    [JsonPropertyName("Error")]
    public string? Error { get; init; }
}

/// <summary>
/// Mount axis move command
/// </summary>
public record MountAxisMoveCommand
{
    [JsonPropertyName("direction")]
    public string Direction { get; init; } = string.Empty;

    [JsonPropertyName("rate")]
    public double Rate { get; init; }
}

/// <summary>
/// Mount move directions
/// </summary>
public enum MountDirection
{
    East,
    West,
    North,
    South
}

/// <summary>
/// TPPA action types
/// </summary>
public enum TppaAction
{
    StartAlignment,
    StopAlignment,
    PauseAlignment,
    ResumeAlignment
}

/// <summary>
/// TPPA command for polar alignment
/// </summary>
public record TppaCommand
{
    [JsonPropertyName("Action")]
    public string Action { get; init; } = string.Empty;

    [JsonPropertyName("ManualMode")]
    public bool? ManualMode { get; init; }

    [JsonPropertyName("TargetDistance")]
    public int? TargetDistance { get; init; }

    [JsonPropertyName("MoveRate")]
    public int? MoveRate { get; init; }

    [JsonPropertyName("EastDirection")]
    public bool? EastDirection { get; init; }

    [JsonPropertyName("StartFromCurrentPosition")]
    public bool? StartFromCurrentPosition { get; init; }

    [JsonPropertyName("AltDegrees")]
    public int? AltDegrees { get; init; }

    [JsonPropertyName("AltMinutes")]
    public int? AltMinutes { get; init; }

    [JsonPropertyName("AltSeconds")]
    public double? AltSeconds { get; init; }

    [JsonPropertyName("AzDegrees")]
    public int? AzDegrees { get; init; }

    [JsonPropertyName("AzMinutes")]
    public int? AzMinutes { get; init; }

    [JsonPropertyName("AzSeconds")]
    public double? AzSeconds { get; init; }

    [JsonPropertyName("AlignmentTolerance")]
    public double? AlignmentTolerance { get; init; }

    [JsonPropertyName("Filter")]
    public string? Filter { get; init; }

    [JsonPropertyName("ExposureTime")]
    public double? ExposureTime { get; init; }

    [JsonPropertyName("Binning")]
    public int? Binning { get; init; }

    [JsonPropertyName("Gain")]
    public int? Gain { get; init; }

    [JsonPropertyName("Offset")]
    public int? Offset { get; init; }

    [JsonPropertyName("SearchRadius")]
    public double? SearchRadius { get; init; }
}

/// <summary>
/// TPPA alignment error response
/// </summary>
public record TppaAlignmentErrorResponse
{
    [JsonPropertyName("AzimuthError")]
    public double AzimuthError { get; init; }

    [JsonPropertyName("AltitudeError")]
    public double AltitudeError { get; init; }

    [JsonPropertyName("TotalError")]
    public double TotalError { get; init; }
}
