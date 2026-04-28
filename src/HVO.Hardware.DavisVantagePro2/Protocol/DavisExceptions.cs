namespace HVO.Hardware.DavisVantagePro2.Protocol;

/// <summary>Base exception for Davis console communication errors.</summary>
public class DavisException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>Thrown when a packet fails CRC-CCITT-16 validation.</summary>
public class DavisCrcException(string message)
    : DavisException(message);

/// <summary>Thrown when the console does not respond to the wake sequence.</summary>
public class DavisWakeupException(string message)
    : DavisException(message);

/// <summary>Thrown when max retries are exceeded during a command.</summary>
public class DavisRetriesExceededException(string message)
    : DavisException(message);

/// <summary>Thrown when an unexpected response is received from the console.</summary>
public class DavisProtocolException(string message)
    : DavisException(message);

/// <summary>Thrown when the packet type is not recognized.</summary>
public class DavisUnknownPacketTypeException(byte packetType)
    : DavisException($"Unknown LOOP packet type: 0x{packetType:X2}");
