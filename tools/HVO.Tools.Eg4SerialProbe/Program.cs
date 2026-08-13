using System.IO.Ports;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Protocol;
using HVO.Hardware.Eg4.Telemetry;

var statusRequested = args.Length == 3 && args[1] == "status" && args[2] == "--confirm-rs232-com";
var energyRequested = args.Length == 3 && args[1] == "energy" && args[2] == "--confirm-rs232-com";
var mpptRequested = args.Length == 3 && args[1] == "mppt" && args[2] == "--confirm-rs485";
var stableSerial = args.Length > 0 && args[0].StartsWith("/dev/serial/by-id/", StringComparison.Ordinal);
var hidraw = args.Length > 0 && (args[0].StartsWith("/dev/hidraw", StringComparison.Ordinal)
    || args[0].StartsWith("/dev/hvo/eg4-6500ex-", StringComparison.Ordinal));
if (args.Length < 1 || (!stableSerial && !hidraw) ||
    (args.Length != 1 && !statusRequested && !energyRequested && !mpptRequested) ||
    (mpptRequested && !stableSerial))
{
    Console.Error.WriteLine("Usage: HVO.Tools.Eg4SerialProbe </dev/serial/by-id/<adapter>|/dev/hidrawN> [status|energy --confirm-rs232-com | mppt --confirm-rs485]");
    return 2;
}

try
{
    if (mpptRequested)
    {
        await using var coordinator = new Eg4PortCoordinator(
            new Eg4Mppt10048HvSerialTransportFactory(TimeProvider.System));
        var source = new Eg4Mppt10048HvTelemetrySource(coordinator, TimeProvider.System);
        var sample = await source.ReadAsync(new Eg4DeviceOptions
        {
            Type = Eg4DeviceType.ChargeControllerMppt10048Hv,
            SourceId = "eg4-proof-mppt100",
            DeviceId = "proof-mppt100",
            Alias = "MPPT100 proof",
            Port = args[0],
            UnitId = 1,
        }, CancellationToken.None);
        if (!sample.IsAvailable || sample.BatteryObservation is null || sample.MpptDetail?.Trackers.Count != 1)
            throw new InvalidDataException($"MPPT telemetry is unavailable ({sample.UnavailableReason ?? "no observation"}).");
        var tracker = sample.MpptDetail.Trackers[0];
        Console.WriteLine("MPPT100 fixed register response and layout verified.");
        Console.WriteLine($"PV: {tracker.VoltageV:F1} V, {tracker.CurrentA:F1} A, {tracker.PowerW:F0} W; battery: {sample.BatteryObservation.VoltageV:F1} V, {sample.BatteryObservation.CurrentA:F1} A");
        return 0;
    }

    if (hidraw)
    {
        if (!statusRequested && !energyRequested)
            throw new InvalidOperationException("The HID proof requires status|energy --confirm-rs232-com so identity is validated in the same session.");
        if (statusRequested)
        {
            await using var source = new Eg46500ExTelemetrySource(
                new Eg46500ExHidrawTransportFactory(TimeProvider.System),
                TimeProvider.System);
            var observation = await source.ReadAsync(new Eg4DeviceOptions
            {
                Type = Eg4DeviceType.Inverter6500Ex,
                SourceId = "eg4-proof-6500ex",
                DeviceId = "proof-6500ex",
                Alias = "6500EX proof",
                Port = args[0],
                UnitId = 0,
            }, CancellationToken.None);
            Console.WriteLine("Identity and supported firmware layout verified.");
            Console.WriteLine($"Battery: {observation.VoltageV:F2} V; canonical current {observation.CurrentA:+0.##;-0.##;0} A; derived power {observation.PowerW:+0.##;-0.##;0} W; reported SOC {observation.StateOfChargePercent:F0}%");
        }
        else
        {
            await using var transport = new Eg46500ExHidrawTransportFactory(TimeProvider.System).Create(args[0]);
            var hidProtocol = Eg46500ExPi30Protocol.DecodePayload(await transport.ExchangeAsync(Eg46500ExInquiry.ProtocolId, CancellationToken.None));
            var hidModel = Eg46500ExPi30Protocol.DecodePayload(await transport.ExchangeAsync(Eg46500ExInquiry.ModelName, CancellationToken.None));
            var hidGeneralModel = Eg46500ExPi30Protocol.DecodePayload(await transport.ExchangeAsync(Eg46500ExInquiry.GeneralModelName, CancellationToken.None));
            if (hidProtocol != "PI30" || hidModel != "MKS2-6500" || hidGeneralModel != "045")
                throw new InvalidDataException($"Identity mismatch: protocol={hidProtocol}, model={hidModel}, generalModel={hidGeneralModel}.");
            var pvWh = Eg46500ExPi30Protocol.DecodeEnergyWh(await transport.ExchangeAsync(Eg46500ExInquiry.TotalPvEnergy, CancellationToken.None));
            var loadWh = Eg46500ExPi30Protocol.DecodeEnergyWh(await transport.ExchangeAsync(Eg46500ExInquiry.TotalLoadEnergy, CancellationToken.None));
            Console.WriteLine("Identity and lifetime energy response shapes verified.");
            Console.WriteLine($"PV energy: {pvWh / 1000d:F3} kWh; AC load energy: {loadWh / 1000d:F3} kWh");
        }
        return 0;
    }

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
    else if (energyRequested)
    {
        var pvWh = Eg46500ExPi30Protocol.DecodeEnergyWh(ReadFrame(Eg46500ExInquiry.TotalPvEnergy));
        var loadWh = Eg46500ExPi30Protocol.DecodeEnergyWh(ReadFrame(Eg46500ExInquiry.TotalLoadEnergy));
        Console.WriteLine($"PV energy: {pvWh / 1000d:F3} kWh; AC load energy: {loadWh / 1000d:F3} kWh");
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
