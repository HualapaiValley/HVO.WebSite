namespace HVO.Gateway.TplinkKasa.Devices;

public enum KasaProtocolFamily
{
    LegacyKasaTcp9999,
    KasaSmartAuthenticated,
    TapoAuthenticated,
    Matter,
    HomeKit
}

public enum KasaDeviceKind
{
    Auto,
    Plug,
    PowerStrip,
    DualOutlet,
    Switch,
    ThreeWaySwitch,
    Dimmer,
    Bulb,
    Unknown
}

public enum KasaCapability
{
    SwitchState,
    ChildOutlets,
    EnergyRealtime,
    LightState,
    Dimming,
    Color,
    VariableColorTemperature,
    ScheduleMetadata,
    LedState,
    Diagnostics
}

public enum KasaMetadataCapability
{
    EnergyRealtime,
    EnergyTotal,
    ScheduleRead,
    CountdownRead,
    AwayModeRead,
    LedRead,
    FirmwareInfo,
    SignalInfo,
    Diagnostics
}

public enum KasaCommandCapability
{
    SwitchPower,
    DimLevel,
    LightColor,
    LightColorTemperature,
    ScheduleWrite,
    EnergyReset,
    DeviceReset,
    Reboot
}

public enum KasaSafetyClass
{
    TelemetryOnly,
    LowRiskCommand,
    HighRiskCommand,
    SafetyCritical
}
