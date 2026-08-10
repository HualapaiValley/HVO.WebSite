using System.Globalization;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Configuration;

namespace HVO.WebSite.v9.Models;

public static class PowerSystemSnapshotComposer
{
    private const string SolarAssistantSystem = "solarassistant";
    private const string SmartShuntSystem = "victron-smartshunt";

    public static PowerSystemSnapshot Compose(
        IReadOnlyList<PowerReading> readings,
        DateTime observedAtUtc,
        IReadOnlyList<BmsReading>? bmsReadings = null,
        PowerCompositionOptions? options = null,
        IReadOnlyList<PowerMpptDetailPayload>? mpptDetails = null)
    {
        options ??= new PowerCompositionOptions();
        var latestStreams = readings
            .Where(reading => reading.RecordedAt <= observedAtUtc.AddSeconds(options.MaxFutureClockSkewSeconds))
            .GroupBy(reading => (reading.SourceId, reading.DeviceId))
            .Select(group => group.OrderByDescending(reading => reading.RecordedAt).ThenByDescending(reading => reading.Id).First())
            .Where(reading => IsFresh(reading.RecordedAt, observedAtUtc, FreshnessFor(reading.SourceSystem, options), options))
            .ToArray();
        var latestSolarAssistant = LatestBySourceSystem(latestStreams, SolarAssistantSystem, options.PreferredSolarAssistantSourceIds);
        var latestSmartShunt = LatestBySourceSystem(latestStreams, SmartShuntSystem, options.PreferredSmartShuntSourceIds);
        var canonicalSmartShunt = CanonicalSmartShunt(latestSmartShunt);
        var freshBms = (bmsReadings ?? []).Where(reading => IsFresh(
            reading.RecordedAt, observedAtUtc, TimeSpan.FromSeconds(options.JkBmsFreshnessSeconds), options)).ToArray();
        var batteryBanks = ComposeBatteryBanks(freshBms);
        var observations = ComposeObservations(latestStreams, freshBms);
        AddDerivedObservations(observations, options, latestSmartShunt?.SourceId);
        var residual = observations.FirstOrDefault(item => item.SourceId == "derived-inverter-side-residual");
        var inverterSum = observations.FirstOrDefault(item => item.SourceId == "derived-6500ex-branch-sum");
        var notes = residual?.PowerW is { } residualPower && inverterSum?.PowerW is { } directPower
            ? new[] { $"Aggregate inverter residual minus fresh 6500EX branch sum: {residualPower - directPower:0} W; difference may include additional DC loads." }
            : null;

        return new PowerSystemSnapshot(
            ObservedAtUtc: observedAtUtc,
            Ac: ComposeAc(latestSolarAssistant),
            Pv: ComposePv(latestSolarAssistant, mpptDetails ?? [], observedAtUtc, options),
            Battery: ComposeBattery(latestSolarAssistant, canonicalSmartShunt, batteryBanks),
            BatteryBanks: batteryBanks.Count > 0 ? batteryBanks : null,
            Notes: notes,
            BatteryObservations: observations.Count > 0 ? observations : null);
    }

    private static List<PowerBatteryObservation> ComposeObservations(
        IReadOnlyList<PowerReading> readings,
        IReadOnlyList<BmsReading> bmsReadings)
    {
        var observations = new List<PowerBatteryObservation>();
        foreach (var reading in readings.OrderBy(reading => reading.SourceId, StringComparer.OrdinalIgnoreCase))
        {
            var system = reading.SourceSystem?.Trim().ToLowerInvariant();
            var mapped = system switch
            {
                SmartShuntSystem => Observation(reading, PowerMetricSource.VictronSmartShunt, PowerMeasurementRole.BusNet,
                    "battery-bus-net", PowerObservationProvenance.Direct, -reading.BatteryCurrentA, -reading.BatteryPowerW),
                SolarAssistantSystem => Observation(reading, PowerMetricSource.SolarAssistant, PowerMeasurementRole.AggregateEstimate,
                    "solarassistant-battery-aggregate", PowerObservationProvenance.SourceAggregate, reading.BatteryCurrentA, reading.BatteryPowerW),
                PowerSourceSystems.Eg46500Ex => Observation(reading, PowerMetricSource.Eg46500Ex, PowerMeasurementRole.InverterBranch,
                    "inverter-battery-branch", PowerObservationProvenance.Derived, reading.BatteryCurrentA, reading.BatteryPowerW,
                    "PI30 voltage/SOC direct; current=discharge-charge; power=voltage*current", includeSourceInput: true),
                PowerSourceSystems.Eg4Mppt10048Hv => Observation(reading, PowerMetricSource.Eg4Mppt10048Hv, PowerMeasurementRole.ChargeControllerBranch,
                    "charge-controller-battery-branch", PowerObservationProvenance.Direct, reading.BatteryCurrentA, reading.BatteryPowerW),
                _ => null,
            };
            if (mapped is not null) observations.Add(mapped);
        }
        observations.AddRange(bmsReadings.Select(reading =>
        {
            var sourceId = reading.Device?.Address ?? $"jk-bms-{reading.DeviceId}";
            var deviceId = reading.Device?.Alias ?? sourceId;
            return new PowerBatteryObservation(sourceId, deviceId, PowerMetricSource.JkBms, PowerMeasurementRole.BatteryPack,
                "battery-pack", DateTime.SpecifyKind(reading.RecordedAt, DateTimeKind.Utc), reading.PackVoltageMv / 1000.0,
                -reading.CurrentMa / 1000.0, -reading.PowerWatts, reading.SocPercent, PowerObservationProvenance.Direct);
        }));
        return observations;
    }

    private static PowerBatteryObservation? Observation(
        PowerReading reading, PowerMetricSource source, PowerMeasurementRole role, string point,
        PowerObservationProvenance provenance, double? current, double? power,
        string? confidence = null, bool includeSourceInput = false)
    {
        if (reading.BatteryVoltageV is null && current is null && power is null && reading.BatteryStateOfChargePercent is null) return null;
        var observedAtUtc = DateTime.SpecifyKind(reading.RecordedAt, DateTimeKind.Utc);
        return new PowerBatteryObservation(reading.SourceId, reading.DeviceId ?? reading.SourceId, source, role, point,
            observedAtUtc, reading.BatteryVoltageV, current, power,
            reading.BatteryStateOfChargePercent, provenance, confidence,
            includeSourceInput ? [new PowerObservationInput(reading.SourceId, observedAtUtc, reading.DeviceId)] : null);
    }

    private static void AddDerivedObservations(List<PowerBatteryObservation> observations, PowerCompositionOptions options, string? preferredBusSourceId)
    {
        var bus = observations.FirstOrDefault(item => item.Role == PowerMeasurementRole.BusNet && item.SourceId == preferredBusSourceId);
        var expectedMppt = options.EnabledMpptSourceIds ?? [];
        var mppts = expectedMppt.Select(id => observations.FirstOrDefault(item =>
            item.Role == PowerMeasurementRole.ChargeControllerBranch && string.Equals(item.SourceId, id, StringComparison.OrdinalIgnoreCase))).ToArray();
        var completeMppts = mppts.OfType<PowerBatteryObservation>().ToArray();
        var canDeriveCurrent = bus is not null && AllValues([bus, .. completeMppts], item => item.CurrentA);
        var canDerivePower = bus is not null && AllValues([bus, .. completeMppts], item => item.PowerW);
        if (bus is not null && expectedMppt.Count > 0 && completeMppts.Length == expectedMppt.Count &&
            (canDeriveCurrent || canDerivePower) && WithinSkew([bus, .. completeMppts], options))
        {
            var inputs = new[] { bus }.Concat(completeMppts).Select(Input).ToArray();
            observations.Add(new PowerBatteryObservation("derived-inverter-side-residual", "all-inverter-side-loads",
                PowerMetricSource.Derived, PowerMeasurementRole.DerivedAggregate, "inverter-side-residual",
                inputs.Max(input => input.ObservedAtUtc), CurrentA: canDeriveCurrent
                    ? bus.CurrentA - completeMppts.Sum(item => item.CurrentA) : null,
                PowerW: canDerivePower ? bus.PowerW - completeMppts.Sum(item => item.PowerW) : null,
                Provenance: PowerObservationProvenance.Derived, Inputs: inputs));
        }
        var inverters = observations.Where(item => item.Source == PowerMetricSource.Eg46500Ex && item.Role == PowerMeasurementRole.InverterBranch).ToArray();
        var canAggregateCurrent = AllValues(inverters, item => item.CurrentA);
        var canAggregatePower = AllValues(inverters, item => item.PowerW);
        if (inverters.Length > 0 && (canAggregateCurrent || canAggregatePower) && WithinSkew(inverters, options))
        {
            var inputs = inverters.Select(Input).ToArray();
            observations.Add(new PowerBatteryObservation("derived-6500ex-branch-sum", "all-fresh-6500ex-branches",
                PowerMetricSource.Derived, PowerMeasurementRole.DerivedAggregate, "6500ex-branch-sum",
                inputs.Max(input => input.ObservedAtUtc), CurrentA: canAggregateCurrent ? inverters.Sum(item => item.CurrentA) : null,
                PowerW: canAggregatePower ? inverters.Sum(item => item.PowerW) : null,
                Provenance: PowerObservationProvenance.Derived, Inputs: inputs));
        }
    }

    private static bool AllValues(IEnumerable<PowerBatteryObservation> items, Func<PowerBatteryObservation, double?> selector) => items.All(item => selector(item).HasValue);
    private static PowerObservationInput Input(PowerBatteryObservation item) => new(item.SourceId, item.ObservedAtUtc, item.DeviceId);
    private static bool WithinSkew(IEnumerable<PowerBatteryObservation> items, PowerCompositionOptions options)
    {
        var times = items.Select(item => item.ObservedAtUtc).ToArray();
        return times.Length > 0 && times.Max() - times.Min() <= TimeSpan.FromSeconds(options.MaxDerivationSkewSeconds);
    }

    private static bool IsFresh(DateTime timestamp, DateTime now, TimeSpan freshness, PowerCompositionOptions options)
    {
        timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
        return timestamp >= now - freshness && timestamp <= now.AddSeconds(options.MaxFutureClockSkewSeconds);
    }

    private static TimeSpan FreshnessFor(string? system, PowerCompositionOptions options) => TimeSpan.FromSeconds(system?.ToLowerInvariant() switch
    {
        SmartShuntSystem => options.SmartShuntFreshnessSeconds,
        SolarAssistantSystem => options.SolarAssistantFreshnessSeconds,
        PowerSourceSystems.Eg46500Ex or PowerSourceSystems.Eg4Mppt10048Hv => options.Eg4BranchFreshnessSeconds,
        _ => options.SolarAssistantFreshnessSeconds,
    });

    private static PowerReading? CanonicalSmartShunt(PowerReading? reading) => reading is null ? null : new PowerReading
    {
        Id = reading.Id, SourceId = reading.SourceId, SourceSystem = reading.SourceSystem, DeviceId = reading.DeviceId,
        RecordedAt = reading.RecordedAt, BatteryVoltageV = reading.BatteryVoltageV,
        BatteryCurrentA = -reading.BatteryCurrentA, BatteryPowerW = -reading.BatteryPowerW,
        BatteryStateOfChargePercent = reading.BatteryStateOfChargePercent,
    };

    private static PowerSystemAcSnapshot? ComposeAc(PowerReading? solarAssistant)
    {
        if (solarAssistant is null)
            return null;

        return new PowerSystemAcSnapshot(
            LoadPowerW: Sourced(solarAssistant.LoadPowerW, PowerMetricSource.SolarAssistant, solarAssistant),
            GridPowerW: Sourced(solarAssistant.GridPowerW, PowerMetricSource.SolarAssistant, solarAssistant),
            GridFlowDirection: Sourced(FlowFromSignedGridPower(solarAssistant.GridPowerW), PowerMetricSource.SolarAssistant, solarAssistant),
            GridVoltageV: Sourced(solarAssistant.GridVoltageV, PowerMetricSource.SolarAssistant, solarAssistant),
            GridFrequencyHz: Sourced(solarAssistant.GridFrequencyHz, PowerMetricSource.SolarAssistant, solarAssistant),
            OutputVoltageV: Sourced(solarAssistant.OutputVoltageV, PowerMetricSource.SolarAssistant, solarAssistant),
            OutputFrequencyHz: Sourced(solarAssistant.OutputFrequencyHz, PowerMetricSource.SolarAssistant, solarAssistant),
            LoadPercent: Sourced(solarAssistant.LoadPercentage, PowerMetricSource.SolarAssistant, solarAssistant),
            InverterMode: Sourced(solarAssistant.InverterMode, PowerMetricSource.SolarAssistant, solarAssistant),
            OutputSourcePriority: Sourced(solarAssistant.OutputSourcePriority, PowerMetricSource.SolarAssistant, solarAssistant),
            ChargerSourcePriority: Sourced(solarAssistant.ChargerSourcePriority, PowerMetricSource.SolarAssistant, solarAssistant));
    }

    private static PowerSystemPvSnapshot? ComposePv(
        PowerReading? solarAssistant,
        IReadOnlyList<PowerMpptDetailPayload> mpptDetails,
        DateTime observedAtUtc,
        PowerCompositionOptions options)
    {
        var freshDetails = mpptDetails
            .Where(detail => IsFresh(detail.RecordedAtUtc, observedAtUtc,
                FreshnessFor(detail.SourceSystem, options), options))
            .ToArray();
        var trackerCandidates = freshDetails
            .SelectMany(detail => detail.Trackers.Select(tracker => new
            {
                StableId = $"{detail.SourceId}/{tracker.TrackerId}",
                Detail = detail,
                Tracker = tracker,
            }))
            .Where(item => !string.IsNullOrWhiteSpace(item.Detail.SourceId) &&
                !string.IsNullOrWhiteSpace(item.Tracker.TrackerId))
            .ToArray();
        var expectedIds = options.ExpectedPvTrackerIds ?? [];
        var visibleCandidates = expectedIds.Count == 0
            ? trackerCandidates
            : trackerCandidates.Where(item => expectedIds.Contains(item.StableId, StringComparer.OrdinalIgnoreCase)).ToArray();
        var groupedTrackers = visibleCandidates
            .GroupBy(item => item.StableId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var trackers = groupedTrackers
            .Select(group => group
                .OrderByDescending(item => item.Detail.RecordedAtUtc)
                .Select(item => new PowerSystemPvTrackerSnapshot(
                    TrackerId: item.StableId,
                    Name: item.Tracker.Name,
                    SourceId: item.Detail.SourceId,
                    DeviceId: item.Detail.DeviceId,
                    RecordedAtUtc: DateTime.SpecifyKind(item.Detail.RecordedAtUtc, DateTimeKind.Utc),
                    Source: PvSourceFor(item.Detail.SourceSystem),
                    VoltageV: item.Tracker.VoltageV,
                    CurrentA: item.Tracker.CurrentA,
                    PowerW: item.Tracker.PowerW,
                    Provenance: item.Tracker.Provenance,
                    Confidence: item.Tracker.Confidence))
                .First())
            .OrderBy(tracker => tracker.TrackerId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var expectedCandidates = expectedIds
            .Select(expectedId => trackerCandidates.Where(item =>
                string.Equals(item.StableId, expectedId, StringComparison.OrdinalIgnoreCase)).ToArray())
            .ToArray();
        var completeTrackers = expectedCandidates
            .Where(matches => matches.Length == 1 && matches[0].Tracker.PowerW.HasValue)
            .Select(matches => matches[0])
            .ToArray();
        var canDerive = expectedIds.Count > 0 && completeTrackers.Length == expectedIds.Count &&
            WithinSkew(completeTrackers.Select(item => item.Detail.RecordedAtUtc), options);
        SourcedValue<double>? power = null;
        if (canDerive)
        {
            var recordedAt = completeTrackers.Max(item => item.Detail.RecordedAtUtc);
            power = new SourcedValue<double>(
                completeTrackers.Sum(item => item.Tracker.PowerW!.Value),
                PowerMetricSource.Derived,
                DateTime.SpecifyKind(recordedAt, DateTimeKind.Utc),
                "derived-pv-tracker-sum",
                Confidence: $"complete tracker set ({completeTrackers.Length}/{expectedIds.Count})");
        }
        else if (solarAssistant?.PvPowerW is not null)
        {
            power = Sourced(solarAssistant.PvPowerW, PowerMetricSource.SolarAssistant, solarAssistant);
        }

        if (power is null && trackers.Length == 0 && expectedIds.Count == 0)
            return null;

        return new PowerSystemPvSnapshot(
            PowerW: power,
            Trackers: trackers.Length > 0 ? trackers : null,
            ExpectedTrackerCount: expectedIds.Count,
            ReportedTrackerCount: expectedIds.Count == 0
                ? trackers.Length
                : expectedCandidates.Count(matches => matches.Length > 0));
    }

    private static bool WithinSkew(IEnumerable<DateTime> timestamps, PowerCompositionOptions options)
    {
        var times = timestamps.Select(timestamp => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)).ToArray();
        return times.Length > 0 && times.Max() - times.Min() <= TimeSpan.FromSeconds(options.MaxDerivationSkewSeconds);
    }

    private static PowerMetricSource PvSourceFor(string? sourceSystem)
        => sourceSystem?.Trim().ToLowerInvariant() switch
        {
            SolarAssistantSystem => PowerMetricSource.SolarAssistant,
            PowerSourceSystems.Eg46500Ex => PowerMetricSource.Eg46500Ex,
            PowerSourceSystems.Eg4Mppt10048Hv => PowerMetricSource.Eg4Mppt10048Hv,
            _ => PowerMetricSource.Unknown,
        };

    private static PowerSystemBatterySnapshot? ComposeBattery(
        PowerReading? solarAssistant,
        PowerReading? smartShunt,
        IReadOnlyList<PowerSystemBatteryBankSnapshot> batteryBanks)
    {
        if (solarAssistant is null && smartShunt is null && batteryBanks.Count == 0)
            return null;

        var voltageSource = smartShunt?.BatteryVoltageV is not null ? smartShunt : solarAssistant;
        var currentSource = smartShunt?.BatteryCurrentA is not null ? smartShunt : solarAssistant;
        var powerSource = smartShunt?.BatteryPowerW is not null ? smartShunt : solarAssistant;
        var socSource = solarAssistant?.BatteryStateOfChargePercent is not null ? solarAssistant : smartShunt;
        var socConfidence = ReferenceEquals(socSource, smartShunt) ? "fallback-untrusted" : null;

        return new PowerSystemBatterySnapshot(
            StateOfChargePercent: Sourced(socSource?.BatteryStateOfChargePercent, SourceFor(socSource), socSource, socConfidence),
            VoltageV: Sourced(voltageSource?.BatteryVoltageV, SourceFor(voltageSource), voltageSource),
            CurrentA: Sourced(currentSource?.BatteryCurrentA, SourceFor(currentSource), currentSource),
            PowerW: Sourced(powerSource?.BatteryPowerW, SourceFor(powerSource), powerSource),
            FlowDirection: Sourced(FlowFromSignedBatteryPower(powerSource?.BatteryPowerW), SourceFor(powerSource), powerSource),
            CapacityKwh: Sourced(solarAssistant?.BatteryCapacityKwh, PowerMetricSource.SolarAssistant, solarAssistant),
            BankCount: batteryBanks.Count > 0 ? Sourced(batteryBanks.Count, PowerMetricSource.JkBms, batteryBanks.Max(b => b.RecordedAtUtc)) : null,
            HasAlarms: batteryBanks.Count > 0 ? Sourced(batteryBanks.Any(b => b.HasAlarms?.Value == true), PowerMetricSource.JkBms, batteryBanks.Max(b => b.RecordedAtUtc)) : null);
    }

    private static IReadOnlyList<PowerSystemBatteryBankSnapshot> ComposeBatteryBanks(IReadOnlyList<BmsReading> bmsReadings)
        => bmsReadings
            .OrderBy(reading => reading.Device?.Alias ?? reading.Device?.Address ?? reading.DeviceId.ToString(CultureInfo.InvariantCulture))
            .Select(reading =>
            {
                var bankId = !string.IsNullOrWhiteSpace(reading.Device?.Alias)
                    ? reading.Device.Alias
                    : reading.Device?.Address ?? $"device-{reading.DeviceId}";
                var cellVoltages = reading.CellVoltages.Select(v => v.VoltageMv).ToArray();
                var minCellVoltageMv = cellVoltages.Length > 0 ? cellVoltages.Min() : (int?)null;
                var maxCellVoltageMv = cellVoltages.Length > 0 ? cellVoltages.Max() : (int?)null;
                var averageCellVoltageMv = cellVoltages.Length > 0 ? cellVoltages.Average() : (double?)null;

                return new PowerSystemBatteryBankSnapshot(
                    BankId: bankId,
                    RecordedAtUtc: reading.RecordedAt,
                    Source: PowerMetricSource.JkBms,
                    SourceId: reading.Device?.Address,
                    DeviceId: bankId,
                    StateOfChargePercent: Sourced((double)reading.SocPercent, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    VoltageV: Sourced(reading.PackVoltageMv / 1000.0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    CurrentA: Sourced(reading.CurrentMa / 1000.0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    PowerW: Sourced(reading.PowerWatts, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    StateOfHealthPercent: Sourced((double)reading.SohPercent, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    MinCellVoltageV: Sourced(minCellVoltageMv / 1000.0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    MaxCellVoltageV: Sourced(maxCellVoltageMv / 1000.0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    DeltaCellVoltageV: Sourced(reading.DeltaCellVoltageMv / 1000.0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    AverageCellVoltageV: Sourced(averageCellVoltageMv / 1000.0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    BatteryTemperature1C: Sourced(reading.BatteryTemp1C, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    BatteryTemperature2C: Sourced(reading.BatteryTemp2C, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    PowerTubeTemperatureC: Sourced(reading.PowerTubeC, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    BalancingActive: Sourced(reading.BalancingActive, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    BalancingCurrentA: Sourced(reading.BalancingCurrentMa / 1000.0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    HasAlarms: Sourced(reading.AlarmBitmask != 0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId));
            })
            .ToArray();

    private static PowerReading? LatestBySourceSystem(IReadOnlyList<PowerReading> readings, string sourceSystem, IReadOnlyList<string>? preferredSourceIds = null)
        => readings
            .Where(reading => string.Equals(reading.SourceSystem, sourceSystem, StringComparison.OrdinalIgnoreCase))
            .OrderBy(reading => PreferenceIndex(reading.SourceId, preferredSourceIds))
            .ThenByDescending(reading => reading.RecordedAt)
            .ThenByDescending(reading => reading.Id)
            .FirstOrDefault();

    private static int PreferenceIndex(string sourceId, IReadOnlyList<string>? preferredSourceIds)
    {
        if (preferredSourceIds is null || preferredSourceIds.Count == 0) return 0;
        for (var index = 0; index < preferredSourceIds.Count; index++)
            if (string.Equals(sourceId, preferredSourceIds[index], StringComparison.OrdinalIgnoreCase)) return index;
        return int.MaxValue;
    }

    private static PowerMetricSource SourceFor(PowerReading? reading)
        => reading?.SourceSystem?.Trim().ToLowerInvariant() switch
        {
            SolarAssistantSystem => PowerMetricSource.SolarAssistant,
            SmartShuntSystem => PowerMetricSource.VictronSmartShunt,
            _ => PowerMetricSource.Unknown,
        };

    private static SourcedValue<T>? Sourced<T>(T? value, PowerMetricSource source, PowerReading? reading, string? confidence = null)
        where T : struct
        => value.HasValue && reading is not null
            ? new SourcedValue<T>(value.Value, source, reading.RecordedAt, reading.SourceId, reading.DeviceId, confidence)
            : null;

    private static SourcedValue<T>? Sourced<T>(T? value, PowerMetricSource source, DateTime recordedAt, string? sourceId = null, string? deviceId = null)
        where T : struct
        => value.HasValue ? new SourcedValue<T>(value.Value, source, recordedAt, sourceId, deviceId) : null;

    private static SourcedValue<T> Sourced<T>(T value, PowerMetricSource source, DateTime recordedAt, string? sourceId = null, string? deviceId = null)
        where T : struct
        => new(value, source, recordedAt, sourceId, deviceId);

    private static SourcedValue<string>? Sourced(string? value, PowerMetricSource source, PowerReading? reading)
        => !string.IsNullOrWhiteSpace(value) && reading is not null
            ? new SourcedValue<string>(value, source, reading.RecordedAt, reading.SourceId, reading.DeviceId)
            : null;

    private static PowerFlowDirection? FlowFromSignedGridPower(double? gridPowerW)
        => gridPowerW switch
        {
            > 0 => PowerFlowDirection.Import,
            < 0 => PowerFlowDirection.Export,
            0 => PowerFlowDirection.Idle,
            _ => null,
        };

    private static PowerFlowDirection? FlowFromSignedBatteryPower(double? batteryPowerW)
        => batteryPowerW switch
        {
            > 0 => PowerFlowDirection.Discharging,
            < 0 => PowerFlowDirection.Charging,
            0 => PowerFlowDirection.Idle,
            _ => null,
        };
}
