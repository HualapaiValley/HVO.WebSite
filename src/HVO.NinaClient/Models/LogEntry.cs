namespace HVO.NinaClient.Models;

/// <summary>
/// Represents a log entry from NINA application logs
/// </summary>
public class LogEntry
{
    /// <summary>
    /// Timestamp of the log entry
    /// </summary>
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>
    /// Log level (ERROR, WARNING, INFO, DEBUG, TRACE)
    /// </summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>
    /// Source file that generated the log entry
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Member/method name that generated the log entry
    /// </summary>
    public string Member { get; set; } = string.Empty;

    /// <summary>
    /// Line number in the source file
    /// </summary>
    public string Line { get; set; } = string.Empty;

    /// <summary>
    /// Log message content
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
