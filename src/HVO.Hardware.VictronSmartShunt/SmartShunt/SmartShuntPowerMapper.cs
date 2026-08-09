using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.VictronSmartShunt.Configuration;

namespace HVO.Hardware.VictronSmartShunt.SmartShunt;

public static class SmartShuntPowerMapper
{
    public static PowerReadingPayload MapLiveSample(
        SmartShuntLiveSample sample,
        SmartShuntOptions options,
        SmartShuntPrivateOverlay? overlay = null,
        TimeSpan? maxOverlayAge = null)
    {
        var snapshot = ApplyOverlay(ToSnapshot(sample), overlay, maxOverlayAge);

        return new()
        {
            SourceId = options.SourceId,
            SourceSystem = "victron-smartshunt",
            DeviceId = options.DeviceId,
            RecordedAtUtc = sample.RecordedAtUtc,
            BatteryStateOfChargePercent = snapshot.StateOfChargePercent,
            BatteryVoltageV = snapshot.VoltageV,
            BatteryCurrentA = snapshot.CurrentA,
            BatteryPowerW = snapshot.PowerW,
            SystemPowerW = snapshot.PowerW,
        };
    }

    public static SmartShuntDeviceSnapshot ToSnapshot(SmartShuntLiveSample sample)
        => new()
        {
            RecordedAtUtc = sample.RecordedAtUtc,
            DataPath = sample.DataPath,
            PublicSessionActive = sample.PublicSessionActive,
            PrivateEnrichmentActive = sample.PrivateEnrichmentActive,
            StateOfChargePercent = sample.StateOfChargePercent,
            VoltageV = sample.VoltageV,
            CurrentA = sample.CurrentA,
            PowerW = sample.PowerW,
            ConsumedAh = sample.ConsumedAh,
            StarterVoltageV = sample.StarterVoltageV,
            TemperatureC = sample.TemperatureC,
            RemainingMinutes = sample.RemainingMinutes,
        };

    public static SmartShuntDeviceSnapshot ApplyOverlay(
        SmartShuntDeviceSnapshot snapshot,
        SmartShuntPrivateOverlay? overlay,
        TimeSpan? maxOverlayAge = null)
    {
        if (overlay is null)
            return snapshot;

        var overlayIsFresh = IsOverlayFresh(snapshot.RecordedAtUtc, overlay.RecordedAtUtc, maxOverlayAge);
        var useOverlayFields = overlayIsFresh || PublicSocLooksInvalid(snapshot);
        var hasOverlayData = useOverlayFields
            && (overlay.StateOfChargePercent.HasValue
                || overlay.RemainingMinutes.HasValue
                || overlay.DeepestDischargeAh.HasValue
                || overlay.LastDischargeAh.HasValue
                || overlay.AverageDischargeAh.HasValue
                || overlay.TotalChargeCycles.HasValue
                || overlay.FullDischarges.HasValue
                || overlay.CumulativeAhDrawn.HasValue
                || overlay.MinBatteryVoltageV.HasValue
                || overlay.MaxBatteryVoltageV.HasValue
                || overlay.TimeSinceLastFullSeconds.HasValue
                || overlay.Synchronizations.HasValue
                || overlay.LowVoltageAlarms.HasValue
                || overlay.HighVoltageAlarms.HasValue
                || overlay.MinStarterVoltageV.HasValue
                || overlay.MaxStarterVoltageV.HasValue
                || overlay.DischargedEnergyKwh.HasValue
                || overlay.ChargedEnergyKwh.HasValue
                || overlay.AlarmLowVoltageSetV.HasValue
                || overlay.AlarmLowVoltageClearV.HasValue
                || overlay.AlarmHighVoltageSetV.HasValue
                || overlay.AlarmHighVoltageClearV.HasValue
                || overlay.AlarmLowStarterSetV.HasValue
                || overlay.AlarmLowStarterClearV.HasValue
                || overlay.AlarmHighStarterSetV.HasValue
                || overlay.AlarmHighStarterClearV.HasValue
                || overlay.AlarmLowSocSetPercent.HasValue
                || overlay.AlarmLowSocClearPercent.HasValue
                || overlay.StreamingCounter.HasValue
                || overlay.ChargeStatusCoarsePercent.HasValue
                || overlay.CurrentCoarseA.HasValue);

        return new SmartShuntDeviceSnapshot
        {
            RecordedAtUtc = snapshot.RecordedAtUtc,
            DataPath = hasOverlayData ? "public+private" : snapshot.DataPath,
            PublicSessionActive = snapshot.PublicSessionActive,
            PrivateEnrichmentActive = snapshot.PrivateEnrichmentActive,
            StateOfChargePercent = useOverlayFields ? overlay.StateOfChargePercent ?? snapshot.StateOfChargePercent : snapshot.StateOfChargePercent,
            VoltageV = snapshot.VoltageV,
            CurrentA = snapshot.CurrentA,
            PowerW = snapshot.PowerW,
            ConsumedAh = snapshot.ConsumedAh,
            StarterVoltageV = snapshot.StarterVoltageV,
            TemperatureC = snapshot.TemperatureC,
            RemainingMinutes = useOverlayFields ? overlay.RemainingMinutes ?? snapshot.RemainingMinutes : snapshot.RemainingMinutes,
            DeepestDischargeAh = useOverlayFields ? overlay.DeepestDischargeAh : null,
            LastDischargeAh = useOverlayFields ? overlay.LastDischargeAh : null,
            AverageDischargeAh = useOverlayFields ? overlay.AverageDischargeAh : null,
            TotalChargeCycles = useOverlayFields ? overlay.TotalChargeCycles : null,
            FullDischarges = useOverlayFields ? overlay.FullDischarges : null,
            CumulativeAhDrawn = useOverlayFields ? overlay.CumulativeAhDrawn : null,
            MinBatteryVoltageV = useOverlayFields ? overlay.MinBatteryVoltageV : null,
            MaxBatteryVoltageV = useOverlayFields ? overlay.MaxBatteryVoltageV : null,
            TimeSinceLastFullSeconds = useOverlayFields ? overlay.TimeSinceLastFullSeconds : null,
            Synchronizations = useOverlayFields ? overlay.Synchronizations : null,
            LowVoltageAlarms = useOverlayFields ? overlay.LowVoltageAlarms : null,
            HighVoltageAlarms = useOverlayFields ? overlay.HighVoltageAlarms : null,
            MinStarterVoltageV = useOverlayFields ? overlay.MinStarterVoltageV : null,
            MaxStarterVoltageV = useOverlayFields ? overlay.MaxStarterVoltageV : null,
            DischargedEnergyKwh = useOverlayFields ? overlay.DischargedEnergyKwh : null,
            ChargedEnergyKwh = useOverlayFields ? overlay.ChargedEnergyKwh : null,
            AlarmLowVoltageSetV = useOverlayFields ? overlay.AlarmLowVoltageSetV : null,
            AlarmLowVoltageClearV = useOverlayFields ? overlay.AlarmLowVoltageClearV : null,
            AlarmHighVoltageSetV = useOverlayFields ? overlay.AlarmHighVoltageSetV : null,
            AlarmHighVoltageClearV = useOverlayFields ? overlay.AlarmHighVoltageClearV : null,
            AlarmLowStarterSetV = useOverlayFields ? overlay.AlarmLowStarterSetV : null,
            AlarmLowStarterClearV = useOverlayFields ? overlay.AlarmLowStarterClearV : null,
            AlarmHighStarterSetV = useOverlayFields ? overlay.AlarmHighStarterSetV : null,
            AlarmHighStarterClearV = useOverlayFields ? overlay.AlarmHighStarterClearV : null,
            AlarmLowSocSetPercent = useOverlayFields ? overlay.AlarmLowSocSetPercent : null,
            AlarmLowSocClearPercent = useOverlayFields ? overlay.AlarmLowSocClearPercent : null,
            StreamingCounter = useOverlayFields ? overlay.StreamingCounter : null,
            ChargeStatusCoarsePercent = useOverlayFields ? overlay.ChargeStatusCoarsePercent : null,
            CurrentCoarseA = useOverlayFields ? overlay.CurrentCoarseA : null,
        };
    }

    private static bool IsOverlayFresh(DateTime snapshotRecordedAtUtc, DateTime? overlayRecordedAtUtc, TimeSpan? maxOverlayAge)
    {
        if (!maxOverlayAge.HasValue || overlayRecordedAtUtc is null)
            return true;

        return overlayRecordedAtUtc.Value >= snapshotRecordedAtUtc
            || snapshotRecordedAtUtc - overlayRecordedAtUtc.Value <= maxOverlayAge.Value;
    }

    private static bool PublicSocLooksInvalid(SmartShuntDeviceSnapshot snapshot)
        => snapshot.StateOfChargePercent.HasValue
           && snapshot.StateOfChargePercent.Value == 0
           && snapshot.VoltageV.HasValue
           && snapshot.VoltageV.Value > 1
           && snapshot.CurrentA.HasValue;
}
