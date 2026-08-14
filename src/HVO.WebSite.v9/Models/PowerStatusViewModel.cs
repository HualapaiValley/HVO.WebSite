using System.Globalization;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.Themes.Components.Format;
using HVO.WebSite.v9.Configuration;

namespace HVO.WebSite.v9.Models;

public sealed record PowerStatusViewModel(
    string PvPower,
    string LoadPower,
    string BatteryPower,
    string BatteryFlow,
    string BatteryStateOfCharge,
    string BatterySource,
    string BatterySocSource,
    string InverterMode,
    string BatteryBankCount,
    string BatteryAlarmState,
    string BatteryFreshnessState,
    string PvCompleteness,
    string PvAggregateSource,
    IReadOnlyList<PowerStatusPvTrackerViewModel> PvTrackers,
    IReadOnlyList<PowerStatusBankViewModel> BatteryBanks,
    IReadOnlyList<PowerStatusBatteryObservationViewModel> BatteryObservations,
    IReadOnlyList<string> BatteryObservationNotes,
    string ObservedAt,
    string SnapshotState)
{
    public static PowerStatusViewModel Empty { get; } = new(
        PvPower: "--",
        LoadPower: "--",
        BatteryPower: "--",
        BatteryFlow: "Unknown",
        BatteryStateOfCharge: "--",
        BatterySource: "No source",
        BatterySocSource: "No source",
        InverterMode: "Unknown",
        BatteryBankCount: "--",
        BatteryAlarmState: "Unknown",
        BatteryFreshnessState: "Unknown",
        PvCompleteness: "No PV inputs",
        PvAggregateSource: "No source",
        PvTrackers: [],
        BatteryBanks: [],
        BatteryObservations: [],
        BatteryObservationNotes: [],
        ObservedAt: "Waiting for power telemetry",
        SnapshotState: "Waiting");

    public static PowerStatusViewModel FromSnapshot(PowerSystemSnapshot? snapshot, PowerCompositionOptions? options = null)
    {
        if (snapshot is null)
            return Empty;

        options ??= new PowerCompositionOptions();
        var batteryPower = snapshot.Battery?.PowerW;

        var batteryBanks = FormatBanks(snapshot.BatteryBanks, snapshot.ObservedAtUtc);
        var batteryObservations = FormatObservations(snapshot, options);
        var pvTrackers = FormatPvTrackers(snapshot.Pv?.Trackers, snapshot.ObservedAtUtc);

        return new PowerStatusViewModel(
            PvPower: FormatWatts(snapshot.Pv?.PowerW?.Value),
            LoadPower: FormatWatts(snapshot.Ac?.LoadPowerW?.Value),
            BatteryPower: FormatBatteryFacingWatts(batteryPower?.Value),
            BatteryFlow: FormatFlow(snapshot.Battery?.FlowDirection?.Value),
            BatteryStateOfCharge: FormatPercent(snapshot.Battery?.StateOfChargePercent?.Value),
            BatterySource: FormatSource(snapshot.Battery?.PowerW),
            BatterySocSource: FormatSource(snapshot.Battery?.StateOfChargePercent),
            InverterMode: snapshot.Ac?.InverterMode?.Value ?? "Unknown",
            BatteryBankCount: FormatBankCount(snapshot.Battery?.BankCount?.Value),
            BatteryAlarmState: FormatAlarmState(snapshot.Battery?.HasAlarms?.Value),
            BatteryFreshnessState: FormatBankFreshnessState(batteryBanks),
            PvCompleteness: FormatPvCompleteness(snapshot.Pv),
            PvAggregateSource: FormatPvAggregateSource(snapshot.Pv?.PowerW),
            PvTrackers: pvTrackers,
            BatteryBanks: batteryBanks,
            BatteryObservations: batteryObservations,
            BatteryObservationNotes: snapshot.Notes?
                .Where(note => !string.IsNullOrWhiteSpace(note))
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? [],
            ObservedAt: $"Observed {HvoFormat.Timestamp(snapshot.ObservedAtUtc, "dd MMM yyyy - h:mm tt")}",
            SnapshotState: "Live");
    }

    private static string FormatWatts(double? value)
        => value.HasValue ? $"{value.Value:0} W" : "--";

    private static string FormatSignedWatts(double? value)
        => value.HasValue ? $"{value.Value:+0;-0;0} W" : "--";

    private static string FormatBatteryFacingWatts(double? canonicalValue)
        => canonicalValue.HasValue ? $"{-canonicalValue.Value:+0;-0;0} W" : "--";

    private static string FormatBatteryFacingAmps(double? canonicalValue)
        => canonicalValue.HasValue ? $"{-canonicalValue.Value:+0.0;-0.0;0.0} A" : "--";

    private static string FormatPercent(double? value)
        => value.HasValue ? $"{value.Value:0}%" : "--";

    private static string FormatBankCount(int? value)
        => value switch
        {
            > 1 => $"{value.Value} banks",
            1 => "1 bank",
            _ => "--",
        };

    private static string FormatAlarmState(bool? hasAlarms)
        => hasAlarms switch
        {
            true => "Active alarm",
            false => "No alarms",
            _ => "Unknown",
        };

    private static IReadOnlyList<PowerStatusBankViewModel> FormatBanks(IReadOnlyList<PowerSystemBatteryBankSnapshot>? banks, DateTime observedAtUtc)
        => banks is { Count: > 0 }
            ? banks
                .OrderBy(b => b.BankId, StringComparer.OrdinalIgnoreCase)
                .Select((b, index) => new PowerStatusBankViewModel(
                    BankId: b.BankId,
                    HeadingId: $"power-bank-{index + 1}",
                    Seen: FormatRelativeAge(b.RecordedAtUtc, observedAtUtc),
                    StateOfCharge: FormatPercent(b.StateOfChargePercent?.Value),
                    Voltage: FormatVolts(b.VoltageV?.Value),
                    Current: FormatSignedAmps(b.CurrentA?.Value),
                    Power: FormatSignedWatts(b.PowerW?.Value),
                    StateOfHealth: FormatPercent(b.StateOfHealthPercent?.Value),
                    CellRange: FormatCellRange(b.MinCellVoltageV?.Value, b.MaxCellVoltageV?.Value),
                    AverageCellVoltage: HvoFormat.Voltage(b.AverageCellVoltageV?.Value, 3),
                    DeltaCellVoltage: FormatMillivolts(b.DeltaCellVoltageV?.Value),
                    Temperatures: FormatTemperatures(b.BatteryTemperature1C?.Value, b.BatteryTemperature2C?.Value, b.PowerTubeTemperatureC?.Value),
                    Balancing: FormatBalancing(b.BalancingActive?.Value, b.BalancingCurrentA?.Value),
                    FreshnessStatus: FreshnessStatusFor(b.RecordedAtUtc, observedAtUtc),
                    AlarmState: FormatAlarmState(b.HasAlarms?.Value),
                    IsAlarmed: b.HasAlarms?.Value == true))
                .ToArray()
            : [];

    private static string FormatVolts(double? value)
        => value.HasValue ? $"{value.Value:0.00} V" : "--";

    private static string FormatSignedAmps(double? value)
        => value.HasValue ? $"{value.Value:+0.0;-0.0;0.0} A" : "--";

    private static string FormatMillivolts(double? value)
        => value.HasValue ? $"{value.Value * 1000:0} mV" : "--";

    private static string FormatCellRange(double? minimum, double? maximum)
        => minimum.HasValue || maximum.HasValue
            ? $"{HvoFormat.Voltage(minimum, 3)} to {HvoFormat.Voltage(maximum, 3)}"
            : "--";

    private static string FormatTemperatures(double? battery1, double? battery2, double? powerTube)
    {
        var values = new[]
        {
            battery1.HasValue ? $"B1 {HvoFormat.Temperature(battery1)}" : null,
            battery2.HasValue ? $"B2 {HvoFormat.Temperature(battery2)}" : null,
            powerTube.HasValue ? $"Power {HvoFormat.Temperature(powerTube)}" : null,
        }.OfType<string>().ToArray();

        return values.Length > 0 ? string.Join("; ", values) : "--";
    }

    private static string FormatBalancing(bool? active, double? current)
        => active switch
        {
            true => current.HasValue ? $"Active ({HvoFormat.SignedCurrent(current)})" : "Active",
            false => "Inactive",
            _ => "Unknown",
        };

    private static string FormatRelativeAge(DateTime recordedAtUtc, DateTime referenceUtc)
    {
        var age = referenceUtc - recordedAtUtc;
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        var minutes = (int)age.TotalMinutes;
        if (minutes < 1)
            return "just now";

        if (minutes < 60)
            return $"{minutes} min ago";

        var hours = (int)age.TotalHours;
        return hours < 24
            ? $"{hours} hr ago"
            : $"{age.TotalDays:0.0} days ago";
    }

    private static string FreshnessStatusFor(DateTime recordedAtUtc, DateTime referenceUtc)
    {
        var age = referenceUtc - recordedAtUtc;
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        return age.TotalMinutes switch
        {
            >= 15 => "stale",
            >= 5 => "warning",
            _ => "fresh",
        };
    }

    private static string FormatBankFreshnessState(IReadOnlyList<PowerStatusBankViewModel> banks)
    {
        if (banks.Count == 0)
            return "Unknown";

        var staleCount = banks.Count(b => b.FreshnessStatus == "stale");
        if (staleCount > 0)
            return staleCount == 1 ? "1 stale bank" : $"{staleCount} stale banks";

        var warningCount = banks.Count(b => b.FreshnessStatus == "warning");
        if (warningCount > 0)
            return warningCount == 1 ? "1 aging bank" : $"{warningCount} aging banks";

        return "All banks fresh";
    }

    private static string FormatFlow(PowerFlowDirection? direction)
        => direction switch
        {
            PowerFlowDirection.Import => "Importing",
            PowerFlowDirection.Export => "Exporting",
            PowerFlowDirection.Charging => "Charging",
            PowerFlowDirection.Discharging => "Discharging",
            PowerFlowDirection.Idle => "Idle",
            _ => "Unknown",
        };

    private static IReadOnlyList<PowerStatusBatteryObservationViewModel> FormatObservations(
        PowerSystemSnapshot snapshot,
        PowerCompositionOptions options)
        => snapshot.BatteryObservations?
            .OrderBy(observation => observation.Role)
            .ThenBy(observation => observation.SourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(observation => observation.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(observation => FormatObservation(observation, snapshot, options))
            .ToArray() ?? [];

    private static PowerStatusBatteryObservationViewModel FormatObservation(
        PowerBatteryObservation observation,
        PowerSystemSnapshot snapshot,
        PowerCompositionOptions options)
    {
        var freshness = FormatObservationFreshness(observation, snapshot.ObservedAtUtc, options);
        return new PowerStatusBatteryObservationViewModel(
            Source: FormatSourceName(observation.Source),
            SourceId: observation.SourceId,
            DeviceId: observation.DeviceId,
            Role: FormatRole(observation.Role),
            MeasurementPoint: observation.MeasurementPoint,
            Voltage: HvoFormat.Voltage(FiniteOrNull(observation.VoltageV)),
            Current: FormatBatteryFacingAmps(FiniteOrNull(observation.CurrentA)),
            Power: FormatBatteryFacingWatts(FiniteOrNull(observation.PowerW)),
            StateOfCharge: HvoFormat.Percent(FiniteOrNull(observation.StateOfChargePercent)),
            Flow: FormatCanonicalFlow(observation.CurrentA ?? observation.PowerW),
            Freshness: freshness.Label,
            FreshnessStatus: freshness.Status,
            SelectionLabels: FormatSelectionLabels(observation, snapshot.Battery),
            Provenance: FormatProvenance(observation),
            ProvenanceDetail: FormatProvenanceDetail(observation));
    }

    private static (string Label, string Status) FormatObservationFreshness(
        PowerBatteryObservation observation,
        DateTime referenceUtc,
        PowerCompositionOptions options)
    {
        if (HasInvalidValues(observation))
            return ("Invalid values", "invalid");

        var effectiveObservedAtUtc = observation.Provenance == PowerObservationProvenance.Derived
            && observation.Inputs is { Count: > 0 }
                ? observation.Inputs.Min(input => input.ObservedAtUtc)
                : observation.ObservedAtUtc;
        var age = referenceUtc - effectiveObservedAtUtc;
        if (age < TimeSpan.FromSeconds(-options.MaxFutureClockSkewSeconds))
            return ("Future timestamp", "invalid");

        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        var threshold = TimeSpan.FromSeconds(observation.Source switch
        {
            PowerMetricSource.VictronSmartShunt => options.SmartShuntFreshnessSeconds,
            PowerMetricSource.JkBms => options.JkBmsFreshnessSeconds,
            PowerMetricSource.Eg46500Ex or PowerMetricSource.Eg4Mppt10048Hv => options.Eg4BranchFreshnessSeconds,
            PowerMetricSource.Derived => options.Eg4BranchFreshnessSeconds,
            _ => options.Eg4BranchFreshnessSeconds,
        });

        var status = age > threshold ? "stale" : age >= threshold * 0.8 ? "warning" : "fresh";
        return ($"{FormatRelativeAge(effectiveObservedAtUtc, referenceUtc)} ({status})", status);
    }

    private static IReadOnlyList<string> FormatSelectionLabels(
        PowerBatteryObservation observation,
        PowerSystemBatterySnapshot? battery)
    {
        var labels = new List<string>();
        var busMetrics = new List<string>();
        AddSelectedMetric(busMetrics, "voltage", observation.VoltageV, observation, battery?.VoltageV);
        AddSelectedMetric(busMetrics, "current", observation.CurrentA, observation, battery?.CurrentA);
        AddSelectedMetric(busMetrics, "power", observation.PowerW, observation, battery?.PowerW);
        if (busMetrics.Count > 0)
        {
            var preference = observation.Source switch
            {
                PowerMetricSource.VictronSmartShunt => "Preferred bus",
                PowerMetricSource.SolarAssistant => "Fallback aggregate",
                _ => "Selected electrical",
            };
            labels.Add($"{preference}: {string.Join(", ", busMetrics)}");
        }

        if (observation.StateOfChargePercent.HasValue && Matches(observation, battery?.StateOfChargePercent))
        {
            labels.Add(observation.Source == PowerMetricSource.VictronSmartShunt
                ? "Preferred SOC"
                : observation.Source == PowerMetricSource.JkBms
                    ? "Fallback SOC"
                    : "Selected SOC");
        }

        return labels.Count > 0 ? labels : ["Comparison only"];
    }

    private static void AddSelectedMetric<T>(
        List<string> metrics,
        string label,
        double? observationValue,
        PowerBatteryObservation observation,
        SourcedValue<T>? selected)
    {
        if (observationValue.HasValue && Matches(observation, selected))
            metrics.Add(label);
    }

    private static bool Matches<T>(PowerBatteryObservation observation, SourcedValue<T>? selected)
    {
        if (selected is null || observation.Source != selected.Source)
            return false;

        if (!string.IsNullOrWhiteSpace(selected.SourceId)
            && !string.Equals(observation.SourceId, selected.SourceId, StringComparison.OrdinalIgnoreCase))
            return false;

        return string.IsNullOrWhiteSpace(selected.DeviceId)
            || string.Equals(observation.DeviceId, selected.DeviceId, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatCanonicalFlow(double? value)
        => value switch
        {
            > 0 => "Discharging (out of battery)",
            < 0 => "Charging (into battery)",
            0 => "Idle",
            _ => "Unknown",
        };

    private static IReadOnlyList<PowerStatusPvTrackerViewModel> FormatPvTrackers(
        IReadOnlyList<PowerSystemPvTrackerSnapshot>? trackers,
        DateTime observedAtUtc) => trackers?
        .OrderBy(tracker => tracker.TrackerId, StringComparer.OrdinalIgnoreCase)
        .Select(tracker => new PowerStatusPvTrackerViewModel(
            TrackerId: tracker.TrackerId,
            Name: tracker.Name,
            Source: FormatSourceName(tracker.Source),
            Power: FormatWatts(tracker.PowerW),
            Voltage: FormatVolts(tracker.VoltageV),
            Current: tracker.CurrentA.HasValue ? $"{tracker.CurrentA.Value:0.0} A" : "--",
            Freshness: FormatRelativeAge(tracker.RecordedAtUtc, observedAtUtc),
            Provenance: tracker.Provenance == PowerObservationProvenance.Direct ? "Direct" : "Derived",
            Confidence: tracker.Confidence ?? "No confidence detail"))
        .ToArray() ?? [];

    private static string FormatPvCompleteness(PowerSystemPvSnapshot? pv) => pv switch
    {
        null => "No PV inputs",
        { ExpectedTrackerCount: > 0 } => $"{pv.ReportedTrackerCount} of {pv.ExpectedTrackerCount} inputs",
        { Trackers.Count: > 0 } => $"{pv.Trackers.Count} input(s)",
        _ => "No independent inputs",
    };

    private static string FormatPvAggregateSource(SourcedValue<double>? aggregate)
    {
        if (aggregate is null)
            return "No aggregate";
        var source = FormatSourceName(aggregate.Source);
        return string.IsNullOrWhiteSpace(aggregate.Confidence) ? source : $"{source}; {aggregate.Confidence}";
    }

    private static string FormatRole(PowerMeasurementRole role)
        => role switch
        {
            PowerMeasurementRole.BusNet => "Battery bus net",
            PowerMeasurementRole.BatteryPack => "Battery pack",
            PowerMeasurementRole.InverterBranch => "Inverter branch",
            PowerMeasurementRole.ChargeControllerBranch => "Charge-controller branch",
            PowerMeasurementRole.AggregateEstimate => "Aggregate estimate",
            PowerMeasurementRole.DerivedAggregate => "Derived aggregate",
            _ => "Unknown role",
        };

    private static string FormatProvenance(PowerBatteryObservation observation)
        => observation.Provenance switch
        {
            PowerObservationProvenance.Direct => "Direct",
            PowerObservationProvenance.SourceAggregate => "Source aggregate",
            PowerObservationProvenance.Derived => "Derived",
            _ => "Unknown",
        };

    private static string FormatProvenanceDetail(PowerBatteryObservation observation)
    {
        var confidence = string.IsNullOrWhiteSpace(observation.Confidence) ? null : observation.Confidence;
        var inputs = observation.Inputs?
            .GroupBy(input => $"{input.SourceId}\0{input.DeviceId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(input => string.IsNullOrWhiteSpace(input.DeviceId)
                ? input.SourceId
                : $"{input.SourceId}/{input.DeviceId}")
            .ToArray() ?? [];
        if (inputs.Length > 0)
        {
            var inputDetail = $"Inputs: {string.Join(", ", inputs)}";
            return confidence is null ? inputDetail : $"{confidence}; {inputDetail}";
        }
        return confidence ?? observation.MeasurementPoint;
    }

    private static bool HasInvalidValues(PowerBatteryObservation observation)
        => !IsFinite(observation.VoltageV)
           || !IsFinite(observation.CurrentA)
           || !IsFinite(observation.PowerW)
           || !IsFinite(observation.StateOfChargePercent)
           || observation.VoltageV < 0
           || observation.StateOfChargePercent is < 0 or > 100;

    private static bool IsFinite(double? value) => !value.HasValue || double.IsFinite(value.Value);

    private static double? FiniteOrNull(double? value) => IsFinite(value) ? value : null;

    private static string FormatSource<T>(SourcedValue<T>? value)
    {
        if (value is null)
            return "No source";

        var source = FormatSourceName(value.Source);
        return string.IsNullOrWhiteSpace(value.SourceId) ? source : $"{source} ({value.SourceId})";
    }

    private static string FormatSourceName(PowerMetricSource source)
        => source switch
        {
            PowerMetricSource.SolarAssistant => "SolarAssistant",
            PowerMetricSource.JkBms => "JK BMS",
            PowerMetricSource.VictronSmartShunt => "SmartShunt",
            PowerMetricSource.Derived => "Derived",
            PowerMetricSource.Eg46500Ex => "EG4 6500EX",
            PowerMetricSource.Eg4Mppt10048Hv => "EG4 MPPT100-48HV",
            _ => "No source",
        };
}

public sealed record PowerStatusBankViewModel(
    string BankId,
    string HeadingId,
    string Seen,
    string StateOfCharge,
    string Voltage,
    string Current,
    string Power,
    string StateOfHealth,
    string CellRange,
    string AverageCellVoltage,
    string DeltaCellVoltage,
    string Temperatures,
    string Balancing,
    string FreshnessStatus,
    string AlarmState,
    bool IsAlarmed);

public sealed record PowerStatusBatteryObservationViewModel(
    string Source,
    string SourceId,
    string DeviceId,
    string Role,
    string MeasurementPoint,
    string Voltage,
    string Current,
    string Power,
    string StateOfCharge,
    string Flow,
    string Freshness,
    string FreshnessStatus,
    IReadOnlyList<string> SelectionLabels,
    string Provenance,
    string ProvenanceDetail);

public sealed record PowerStatusPvTrackerViewModel(
    string TrackerId,
    string Name,
    string Source,
    string Power,
    string Voltage,
    string Current,
    string Freshness,
    string Provenance,
    string Confidence);

public sealed record PowerPvStringViewModel(
    string StringId,
    string Power,
    string Voltage,
    string Current);
