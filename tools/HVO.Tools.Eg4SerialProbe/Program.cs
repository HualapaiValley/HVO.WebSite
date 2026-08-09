using System.IO.Ports;
using HVO.Hardware.Eg4.Protocol;

var statusRequested = args.Length == 3 && args[1] == "status" && args[2] == "--confirm-rs232-com";
if (args.Length < 1 || !args[0].StartsWith("/dev/serial/by-id/", StringComparison.Ordinal) ||
    (args.Length != 1 && !statusRequested))
{
    Console.Error.WriteLine("Usage: HVO.Tools.Eg4SerialProbe /dev/serial/by-id/<adapter> [status --confirm-rs232-com]");
    return 2;
}

try
{
    using var port = new SerialPort(args[0], 2400, Parity.None, 8, StopBits.One)
    {
        ReadTimeout = 1500,
        WriteTimeout = 1500,
    };
    port.Open();

    string Exchange(Eg46500ExInquiry inquiry)
        => Eg46500ExPi30Protocol.DecodePayload(ReadFrame(inquiry));

    var protocol = Exchange(Eg46500ExInquiry.ProtocolId);
    var model = Exchange(Eg46500ExInquiry.ModelName);
    var generalModel = Exchange(Eg46500ExInquiry.GeneralModelName);
    if (protocol != "PI30" || model != "MKS2-6500" || generalModel != "045")
        throw new InvalidDataException($"Identity mismatch: protocol={protocol}, model={model}, generalModel={generalModel}.");
    Console.WriteLine($"Identity verified: {protocol} {model} model {generalModel}");
    Console.WriteLine($"Main firmware: {Exchange(Eg46500ExInquiry.MainFirmware)}");
    Console.WriteLine($"Secondary firmware: {Exchange(Eg46500ExInquiry.SecondaryFirmware)}");

    if (statusRequested)
    {
        var status = Eg46500ExPi30Protocol.DecodeGeneralStatus(ReadFrame(Eg46500ExInquiry.GeneralStatus));
        Console.WriteLine($"Battery: {status.VoltageV:F2} V; charge {status.ChargingCurrentA} A; discharge {status.DischargingCurrentA} A; reported SOC {status.ReportedStateOfChargePercent}%");
    }
    return 0;

    byte[] ReadFrame(Eg46500ExInquiry inquiry)
    {
        var request = Eg46500ExPi30Protocol.Encode(inquiry);
        port.Write(request, 0, request.Length);
        var response = new List<byte>();
        while (response.Count < 512)
        {
            var value = port.ReadByte();
            response.Add((byte)value);
            if (value == 0x0D) return [.. response];
        }
        throw new InvalidDataException("PI30 response exceeded 512 bytes without CR framing.");
    }
}
catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or TimeoutException or Eg4TransportException)
{
    Console.Error.WriteLine($"EG4 proof failed: {exception.Message}");
    return 1;
}
