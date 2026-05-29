using System.Net.Sockets;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Devices;

internal static class KasaFailureMessages
{
    public static string DescribeReadFailure(Exception ex) => ex switch
    {
        OperationCanceledException or TimeoutException => "Operation canceled or timed out.",
        SocketException => "Network socket failure.",
        IOException => "Network I/O failure.",
        InvalidDataException => "Protocol framing or payload failure.",
        JsonException => "Invalid JSON response.",
        _ => "Read failure."
    };
}
