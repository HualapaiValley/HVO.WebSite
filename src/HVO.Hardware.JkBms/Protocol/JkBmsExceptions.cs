namespace HVO.Hardware.JkBms.Protocol;

/// <summary>
/// Exceptions thrown by the JK BMS protocol and client layers.
/// </summary>

/// <summary>Base class for all JK BMS protocol exceptions.</summary>
public class JkBmsException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Thrown when the BLE connection to a JK BMS device cannot be established
/// after exhausting all inner retry attempts.
/// </summary>
public sealed class JkBmsConnectException(string address, int attempts, Exception? inner = null)
    : JkBmsException($"Failed to connect to JK BMS at {address} after {attempts} attempt(s).", inner)
{
    public string Address { get; } = address;
    public int Attempts { get; } = attempts;
}

/// <summary>
/// Thrown when a response frame has an invalid or unrecognised start-of-frame sequence.
/// </summary>
public sealed class JkBmsFrameException(string message)
    : JkBmsException(message);

/// <summary>
/// Thrown when a response frame fails CRC32 validation.
/// </summary>
public sealed class JkBmsCrcException(string message)
    : JkBmsException(message);

/// <summary>
/// Thrown when no complete frame can be assembled from received notifications
/// within the allowed timeout.
/// </summary>
public sealed class JkBmsTimeoutException(string address)
    : JkBmsException($"Timed out waiting for response from JK BMS at {address}.")
{
    public string Address { get; } = address;
}
