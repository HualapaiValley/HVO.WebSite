using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Represents the status of a flat capture operation
/// </summary>
public class FlatCaptureStatus
{
    /// <summary>
    /// The number of iterations completed
    /// </summary>
    [JsonPropertyName("completedIterations")]
    public int CompletedIterations { get; set; }

    /// <summary>
    /// The total number of iterations
    /// </summary>
    [JsonPropertyName("totalIterations")]
    public int TotalIterations { get; set; }

    /// <summary>
    /// The current state of the flat capture process
    /// </summary>
    [JsonPropertyName("state")]
    public FlatCaptureState State { get; set; }
}

/// <summary>
/// Enumeration of flat capture states
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FlatCaptureState
{
    /// <summary>
    /// The flat capture process is running
    /// </summary>
    Running,

    /// <summary>
    /// The flat capture process has finished
    /// </summary>
    Finished
}
