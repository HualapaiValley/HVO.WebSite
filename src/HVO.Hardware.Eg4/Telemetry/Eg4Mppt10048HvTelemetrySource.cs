using System.Globalization;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Protocol;

namespace HVO.Hardware.Eg4.Telemetry;

public sealed class Eg4Mppt10048HvTelemetrySource(
    IEg4PortCoordinator coordinator,
    TimeProvider timeProvider) : IEg4DeviceTelemetrySource
{
    public bool Supports(Eg4DeviceType deviceType) =>
        deviceType == Eg4DeviceType.ChargeControllerMppt10048Hv;

    public async ValueTask<Eg4TelemetrySample> ReadAsync(
        Eg4DeviceOptions device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!device.Enabled) throw new InvalidOperationException($"Device '{device.SourceId}' is disabled.");
        if (!Supports(device.Type)) throw new InvalidOperationException($"Device type '{device.Type}' is unsupported by the MPPT adapter.");
        if (device.UnitId != Eg4Mppt10048HvProtocol.UnitId)
            throw new InvalidOperationException("The MPPT100-48HV production mapping is validated only for unit 1.");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var response = await coordinator.ReadRegistersAsync(device.Port, Eg4Mppt10048HvProtocol.Request, cancellationToken);
                return Decode(device, response.Registers, timeProvider.GetUtcNow().UtcDateTime);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Eg4TransportException exception) when (exception.Kind == Eg4TransportFailureKind.Timeout && attempt == 1)
            {
                return Eg4TelemetrySample.Unavailable("timeout");
            }
            catch (Eg4TransportException exception) when (attempt == 0 && exception.Kind is
                Eg4TransportFailureKind.Crc or Eg4TransportFailureKind.MalformedFrame or
                Eg4TransportFailureKind.Disconnected or Eg4TransportFailureKind.Timeout)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeProvider, cancellationToken);
            }
        }
        throw new InvalidOperationException("The MPPT retry loop completed without a result.");
    }

    internal static Eg4TelemetrySample Decode(
        Eg4DeviceOptions device,
        IReadOnlyList<ushort> registers,
        DateTime observedAtUtc)
    {
        if (registers.Count != Eg4Mppt10048HvProtocol.RegisterCount)
            throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, "MPPT register response must contain exactly 18 words.");
        ValidateLayout(registers);
        var batteryVoltage = Scaled(registers[2], 10);
        var batteryCurrent = -Scaled(registers[3], 10);
        var batteryPower = batteryVoltage * batteryCurrent;
        var pvVoltage = Scaled(registers[7], 10);
        var pvCurrent = Scaled(registers[8], 10);
        var pvPower = (double)registers[9];
        var observedInput = new PowerObservationInput(device.SourceId, observedAtUtc, device.DeviceId);
        var observation = new PowerBatteryObservation(
            device.SourceId,
            device.DeviceId,
            PowerMetricSource.Eg4Mppt10048Hv,
            PowerMeasurementRole.ChargeControllerBranch,
            "controller-battery-output",
            observedAtUtc,
            batteryVoltage,
            batteryCurrent,
            batteryPower,
            StateOfChargePercent: null,
            PowerObservationProvenance.Derived,
            "voltage/current direct; canonical charging sign; power=voltage*current",
            [observedInput]);
        var detail = new PowerMpptDetailPayload
        {
            SourceId = device.SourceId,
            SourceSystem = "eg4-mppt100-48hv",
            DeviceId = device.DeviceId,
            RecordedAtUtc = observedAtUtc,
            Trackers =
            [
                new PowerMpptTrackerDetail
                {
                    TrackerId = "mppt-1",
                    Name = "MPPT 1",
                    VoltageV = pvVoltage,
                    CurrentA = pvCurrent,
                    PowerW = pvPower,
                    Provenance = PowerObservationProvenance.Direct,
                    Confidence = "direct controller registers",
                },
            ],
            BatteryOutput = new PowerMpptBatteryOutputDetail
            {
                VoltageV = batteryVoltage,
                CurrentA = batteryCurrent,
                PowerW = batteryPower,
                Provenance = PowerObservationProvenance.Derived,
                Confidence = "power derived from direct voltage/current",
            },
            Temperatures =
            [
                Temperature("controller", "Controller", registers[10]),
                Temperature("controller-secondary", "Controller secondary", registers[11]),
            ],
            Diagnostics = DiagnosticRegisters(registers),
        };
        return Eg4TelemetrySample.Available(observation, detail);
    }

    private static IReadOnlyList<PowerMpptDiagnosticDetail> DiagnosticRegisters(IReadOnlyList<ushort> registers)
    {
        var fields = new (int Offset, string Key, string Name)[]
        {
            (0, "r200", "Controller status"),
            (1, "r201", "Controller diagnostic 201"),
            (4, "r204", "Charge state"),
            (5, "controllerEstimatedSocPercent", "Controller estimated SOC percent"),
            (6, "r206", "Controller diagnostic 206"),
            (12, "r212", "Controller diagnostic 212"),
            (15, "r215", "Controller diagnostic 215"),
            (16, "r216", "Controller diagnostic 216"),
            (17, "r217", "Controller diagnostic 217"),
        };
        return fields.Select(field => new PowerMpptDiagnosticDetail
        {
            Key = field.Key,
            Name = field.Name,
            Value = registers[field.Offset].ToString(CultureInfo.InvariantCulture),
        }).ToArray();
    }

    private static PowerMpptTemperatureDetail Temperature(string id, string name, ushort word) => new()
    {
        TemperatureId = id,
        Name = name,
        TemperatureC = word,
        Confidence = "direct neutral Celsius register",
    };

    private static double Scaled(ushort word, double divisor) => word / divisor;

    private static void ValidateLayout(IReadOnlyList<ushort> registers)
    {
        var batteryVoltage = Scaled(registers[2], 10);
        var chargingCurrent = Scaled(registers[3], 10);
        var pvVoltage = Scaled(registers[7], 10);
        var pvCurrent = Scaled(registers[8], 10);
        var expectedPvPower = pvVoltage * pvCurrent;
        if (batteryVoltage is < 30 or > 70 || chargingCurrent > 150 || pvVoltage > 500 || pvCurrent > 50 || registers[9] > 10_000 ||
            registers[5] is < 5 or > 100 || registers[10] > 150 || registers[11] > 150)
        {
            throw new Eg4TransportException(
                Eg4TransportFailureKind.Protocol,
                "MPPT registers 200-217 do not match the validated controller ranges.");
        }

        if ((expectedPvPower > 10 || registers[9] > 20) &&
            Math.Abs(registers[9] - expectedPvPower) > Math.Max(20, expectedPvPower * 0.1))
        {
            throw new Eg4TransportException(
                Eg4TransportFailureKind.Protocol,
                "MPPT PV voltage/current/power registers are internally inconsistent.");
        }
    }
}
