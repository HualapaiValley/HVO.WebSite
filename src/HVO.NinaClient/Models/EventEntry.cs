namespace HVO.NinaClient.Models;

/// <summary>
/// Represents an event entry from NINA event history
/// </summary>
public class EventEntry
{
    /// <summary>
    /// The event name/type that occurred
    /// </summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// The timestamp when the event occurred
    /// </summary>
    public string Time { get; set; } = string.Empty;
}
