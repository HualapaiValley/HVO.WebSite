namespace HVO.Hardware.VictronSmartShunt.SmartShunt;

internal sealed class SmartShuntPrivateFrameDecoder
{
    private string? _serial;
    private string? _firmware;
    private string? _productId;
    private string? _productFamilyRaw;
    private string? _productMetadata;
    private string? _productMetadataRaw;
    private double? _stateOfChargePercent;
    private double? _remainingMinutes;
    private double? _deepestDischargeAh;
    private double? _lastDischargeAh;
    private double? _averageDischargeAh;
    private uint? _totalChargeCycles;
    private uint? _fullDischarges;
    private double? _cumulativeAhDrawn;
    private double? _minBatteryVoltageV;
    private double? _maxBatteryVoltageV;
    private int? _timeSinceLastFullSeconds;
    private uint? _synchronizations;
    private uint? _lowVoltageAlarms;
    private uint? _highVoltageAlarms;
    private double? _minStarterVoltageV;
    private double? _maxStarterVoltageV;
    private double? _dischargedEnergyKwh;
    private double? _chargedEnergyKwh;
    private double? _alarmLowVoltageSetV;
    private double? _alarmLowVoltageClearV;
    private double? _alarmHighVoltageSetV;
    private double? _alarmHighVoltageClearV;
    private double? _alarmLowStarterSetV;
    private double? _alarmLowStarterClearV;
    private double? _alarmHighStarterSetV;
    private double? _alarmHighStarterClearV;
    private double? _alarmLowSocSetPercent;
    private double? _alarmLowSocClearPercent;
    private uint? _streamingCounter;
    private double? _chargeStatusCoarsePercent;
    private double? _currentCoarseA;

    public void Observe(byte[] value)
    {
        var offset = 0;
        while (offset + 6 <= value.Length)
        {
            if (value[offset] is not (0x08 or 0x09) || value[offset + 2] != 0x19)
            {
                offset++;
                continue;
            }

            var categoryPrefix = value[offset + 1];
            var category = value[offset + 3];
            var command = value[offset + 4];
            var isVariableLength = value[offset] == 0x08;
            var lengthType = isVariableLength ? value[offset + 5] : (byte)0x01;
            var payloadLength = isVariableLength ? lengthType & 0x0f : 1;
            var headerLength = isVariableLength ? 6 : 5;
            if (offset + headerLength + payloadLength > value.Length)
            {
                offset++;
                continue;
            }

            var payload = value.AsSpan(offset + headerLength, payloadLength).ToArray();
            offset += headerLength + payloadLength;

            switch ((categoryPrefix, category, command))
            {
                case (0x03, 0x01, 0x02) when payload.Length >= 4:
                    _firmware = DecodeFirmware(payload);
                    break;
                case (0x03, 0x01, 0x0a) when payload.Length > 1:
                    _serial = System.Text.Encoding.ASCII.GetString(payload);
                    break;
                case (0x03, 0x01, 0x00):
                    _productId ??= Convert.ToHexString(payload).ToLowerInvariant();
                    _productFamilyRaw = Convert.ToHexString(payload).ToLowerInvariant();
                    break;
                case (0x03, 0x01, 0x09):
                    _productMetadataRaw = Convert.ToHexString(payload).ToLowerInvariant();
                    break;
                case (0x03, 0x01, 0x50) when payload.Length == 4:
                    _productMetadata = BitConverter.ToInt32(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case (0x03, 0x0f, 0xff) when payload.Length == 2:
                    _stateOfChargePercent = BitConverter.ToUInt16(payload, 0) / 100.0;
                    break;
                case (0x03, 0x0f, 0xfe) when payload.Length == 2:
                    if (!MatchesNotAvailable(payload, "ffff"))
                        _remainingMinutes = BitConverter.ToInt16(payload, 0);
                    break;
                case (0x03, 0x03, 0x00) when payload.Length == 4:
                    _deepestDischargeAh = BitConverter.ToInt32(payload, 0) / 10.0;
                    break;
                case (0x03, 0x03, 0x01) when payload.Length == 4:
                    _lastDischargeAh = BitConverter.ToInt32(payload, 0) / 10.0;
                    break;
                case (0x03, 0x03, 0x02) when payload.Length == 4:
                    _averageDischargeAh = BitConverter.ToInt32(payload, 0) / 10.0;
                    break;
                case (0x03, 0x03, 0x03) when payload.Length == 4:
                    _totalChargeCycles = BitConverter.ToUInt32(payload, 0);
                    break;
                case (0x03, 0x03, 0x04) when payload.Length == 4:
                    _fullDischarges = BitConverter.ToUInt32(payload, 0);
                    break;
                case (0x03, 0x03, 0x05) when payload.Length == 4:
                    _cumulativeAhDrawn = BitConverter.ToInt32(payload, 0) / 10.0;
                    break;
                case (0x03, 0x03, 0x06) when payload.Length == 4:
                    _minBatteryVoltageV = BitConverter.ToInt32(payload, 0) / 100.0;
                    break;
                case (0x03, 0x03, 0x07) when payload.Length == 4:
                    _maxBatteryVoltageV = BitConverter.ToInt32(payload, 0) / 100.0;
                    break;
                case (0x03, 0x03, 0x08) when payload.Length == 4:
                    _timeSinceLastFullSeconds = BitConverter.ToInt32(payload, 0);
                    break;
                case (0x03, 0x03, 0x09) when payload.Length == 4:
                    _synchronizations = BitConverter.ToUInt32(payload, 0);
                    break;
                case (0x03, 0x03, 0x0a) when payload.Length == 4:
                    _lowVoltageAlarms = BitConverter.ToUInt32(payload, 0);
                    break;
                case (0x03, 0x03, 0x0b) when payload.Length == 4:
                    _highVoltageAlarms = BitConverter.ToUInt32(payload, 0);
                    break;
                case (0x03, 0x03, 0x0e) when payload.Length == 4:
                    _minStarterVoltageV = BitConverter.ToInt32(payload, 0) / 100.0;
                    break;
                case (0x03, 0x03, 0x0f) when payload.Length == 4:
                    _maxStarterVoltageV = BitConverter.ToInt32(payload, 0) / 100.0;
                    break;
                case (0x03, 0x03, 0x10) when payload.Length == 4:
                    _dischargedEnergyKwh = BitConverter.ToInt32(payload, 0) / 100.0;
                    break;
                case (0x03, 0x03, 0x11) when payload.Length == 4:
                    _chargedEnergyKwh = BitConverter.ToInt32(payload, 0) / 100.0;
                    break;
                case (0x03, 0x03, 0x20) when payload.Length == 4:
                    _alarmLowVoltageSetV = BitConverter.ToInt32(payload, 0) / 100.0;
                    break;
                case (0x03, 0x03, 0x21) when payload.Length == 4:
                    _alarmLowVoltageClearV = BitConverter.ToInt32(payload, 0) / 100.0;
                    break;
                case (0x03, 0x03, 0x22) when payload.Length == 4:
                    _alarmHighVoltageSetV = BitConverter.ToInt32(payload, 0) / 100.0;
                    break;
                case (0x03, 0x03, 0x23) when payload.Length == 4:
                    _alarmHighVoltageClearV = BitConverter.ToInt32(payload, 0) / 100.0;
                    break;
                case (0x03, 0x03, 0x24) when payload.Length == 4:
                    _alarmLowStarterSetV = BitConverter.ToInt32(payload, 0) / 100.0;
                    break;
                case (0x03, 0x03, 0x25) when payload.Length == 4:
                    _alarmLowStarterClearV = BitConverter.ToInt32(payload, 0) / 100.0;
                    break;
                case (0x03, 0x03, 0x26) when payload.Length == 4:
                    _alarmHighStarterSetV = BitConverter.ToInt32(payload, 0) / 100.0;
                    break;
                case (0x03, 0x03, 0x27) when payload.Length == 4:
                    _alarmHighStarterClearV = BitConverter.ToInt32(payload, 0) / 100.0;
                    break;
                case (0x03, 0x03, 0x28) when payload.Length == 4:
                    _alarmLowSocSetPercent = BitConverter.ToInt32(payload, 0) / 10.0;
                    break;
                case (0x03, 0x03, 0x29) when payload.Length == 4:
                    _alarmLowSocClearPercent = BitConverter.ToInt32(payload, 0) / 10.0;
                    break;
                case (0x03, 0xed, 0x8f) when payload.Length == 2:
                    _currentCoarseA = BitConverter.ToInt16(payload, 0) / 10.0;
                    break;
                case (0x03, 0xec, 0x5a) when payload.Length == 4:
                    _streamingCounter = BitConverter.ToUInt32(payload, 0);
                    break;
                case (0x03, 0xec, 0x87) when payload.Length == 1:
                    _chargeStatusCoarsePercent = payload[0];
                    break;
            }
        }
    }

    public SmartShuntDeviceInfo? Build()
    {
        var overlay = new SmartShuntPrivateOverlay
        {
            RecordedAtUtc = DateTime.UtcNow,
            StateOfChargePercent = _stateOfChargePercent,
            RemainingMinutes = _remainingMinutes,
            DeepestDischargeAh = _deepestDischargeAh,
            LastDischargeAh = _lastDischargeAh,
            AverageDischargeAh = _averageDischargeAh,
            TotalChargeCycles = _totalChargeCycles,
            FullDischarges = _fullDischarges,
            CumulativeAhDrawn = _cumulativeAhDrawn,
            MinBatteryVoltageV = _minBatteryVoltageV,
            MaxBatteryVoltageV = _maxBatteryVoltageV,
            TimeSinceLastFullSeconds = _timeSinceLastFullSeconds,
            Synchronizations = _synchronizations,
            LowVoltageAlarms = _lowVoltageAlarms,
            HighVoltageAlarms = _highVoltageAlarms,
            MinStarterVoltageV = _minStarterVoltageV,
            MaxStarterVoltageV = _maxStarterVoltageV,
            DischargedEnergyKwh = _dischargedEnergyKwh,
            ChargedEnergyKwh = _chargedEnergyKwh,
            AlarmLowVoltageSetV = _alarmLowVoltageSetV,
            AlarmLowVoltageClearV = _alarmLowVoltageClearV,
            AlarmHighVoltageSetV = _alarmHighVoltageSetV,
            AlarmHighVoltageClearV = _alarmHighVoltageClearV,
            AlarmLowStarterSetV = _alarmLowStarterSetV,
            AlarmLowStarterClearV = _alarmLowStarterClearV,
            AlarmHighStarterSetV = _alarmHighStarterSetV,
            AlarmHighStarterClearV = _alarmHighStarterClearV,
            AlarmLowSocSetPercent = _alarmLowSocSetPercent,
            AlarmLowSocClearPercent = _alarmLowSocClearPercent,
            StreamingCounter = _streamingCounter,
            ChargeStatusCoarsePercent = _chargeStatusCoarsePercent,
            CurrentCoarseA = _currentCoarseA,
        };

        if (_serial is null && _firmware is null && _productId is null && _productFamilyRaw is null && _productMetadata is null && _productMetadataRaw is null
            && overlay.StateOfChargePercent is null
            && overlay.RemainingMinutes is null
            && overlay.DeepestDischargeAh is null
            && overlay.LastDischargeAh is null
            && overlay.AverageDischargeAh is null
            && overlay.TotalChargeCycles is null
            && overlay.FullDischarges is null
            && overlay.CumulativeAhDrawn is null
            && overlay.MinBatteryVoltageV is null
            && overlay.MaxBatteryVoltageV is null
            && overlay.TimeSinceLastFullSeconds is null
            && overlay.Synchronizations is null
            && overlay.LowVoltageAlarms is null
            && overlay.HighVoltageAlarms is null
            && overlay.MinStarterVoltageV is null
            && overlay.MaxStarterVoltageV is null
            && overlay.DischargedEnergyKwh is null
            && overlay.ChargedEnergyKwh is null
            && overlay.AlarmLowVoltageSetV is null
            && overlay.AlarmLowVoltageClearV is null
            && overlay.AlarmHighVoltageSetV is null
            && overlay.AlarmHighVoltageClearV is null
            && overlay.AlarmLowStarterSetV is null
            && overlay.AlarmLowStarterClearV is null
            && overlay.AlarmHighStarterSetV is null
            && overlay.AlarmHighStarterClearV is null
            && overlay.AlarmLowSocSetPercent is null
            && overlay.AlarmLowSocClearPercent is null
            && overlay.StreamingCounter is null
            && overlay.ChargeStatusCoarsePercent is null
            && overlay.CurrentCoarseA is null)
            return null;

        return new SmartShuntDeviceInfo
        {
            SerialNumber = _serial,
            FirmwareVersion = _firmware,
            ProductId = _productId,
            ProductFamilyRaw = _productFamilyRaw,
            ProductMetadata = _productMetadata,
            ProductMetadataRaw = _productMetadataRaw,
            Overlay = overlay,
            RecordedAtUtc = DateTime.UtcNow,
        };
    }

    private static string DecodeFirmware(byte[] payload)
    {
        var version = payload.AsSpan(1, 3).ToArray();
        if (version[2] == 0xff && version[1] == 0xff && version[0] == 0xff)
            return "NO FIRMWARE";

        return version[2] != 0
            ? $"v{version[2]}{version[1]:00}.{version[0]:00}"
            : $"v{version[1]}.{version[0]:00}";
    }

    private static bool MatchesNotAvailable(byte[] value, string notAvailableHex)
        => string.Equals(Convert.ToHexString(value), notAvailableHex, StringComparison.OrdinalIgnoreCase);
}
