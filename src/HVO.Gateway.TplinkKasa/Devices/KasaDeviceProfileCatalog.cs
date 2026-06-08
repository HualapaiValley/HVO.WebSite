using HVO.Gateway.TplinkKasa.Configuration;

namespace HVO.Gateway.TplinkKasa.Devices;

public interface IKasaDeviceProfileDefinition
{
    string ProfileId { get; }
    string DisplayName { get; }
    KasaDeviceKind DeviceKind { get; }
    IReadOnlyList<string> ModelPrefixes { get; }
    IReadOnlyList<IKasaDeviceModule> Modules { get; }
    bool Matches(KasaSystemInfo systemInfo);
    KasaDeviceProfile CreateRuntimeProfile(KasaSystemInfo systemInfo, KasaDeviceConfig? config, KasaEnergyReading? energy);
}

public interface IKasaDeviceModule
{
    string ModuleId { get; }
    string DisplayName { get; }
    string Scope { get; }
    KasaSafetyClass SafetyClass { get; }
    IReadOnlySet<KasaCapability> Capabilities { get; }
    IReadOnlySet<KasaMetadataCapability> MetadataCapabilities { get; }
    IReadOnlySet<KasaCommandCapability> CommandCapabilities { get; }
}

public interface IKasaSwitchModule : IKasaDeviceModule { }

public interface IKasaChildOutletModule : IKasaDeviceModule
{
    int? ExpectedOutletCount { get; }
}

public interface IKasaEnergyModule : IKasaDeviceModule
{
    bool HasRealtimePower { get; }
    bool HasVoltage { get; }
    bool HasCurrent { get; }
    bool HasTotalEnergy { get; }
    bool HasHistory { get; }
}

public interface IKasaScheduleModule : IKasaDeviceModule
{
    bool IsChildScoped { get; }
    bool SupportsCountdown { get; }
    bool SupportsAwayMode { get; }
}

public interface IKasaLightModule : IKasaDeviceModule
{
    bool SupportsDimming { get; }
    bool SupportsColor { get; }
    bool SupportsColorTemperature { get; }
    bool HasPreferredStates { get; }
}

public interface IKasaDimmerModule : IKasaDeviceModule
{
    bool HasDefaultBehavior { get; }
    bool HasCalibrationParameters { get; }
}

public interface IKasaDiagnosticsModule : IKasaDeviceModule { }

public static class KasaDeviceProfileCatalog
{
    private static readonly IKasaDeviceProfileDefinition UnknownDefinition = new KasaUnknownProfileDefinition();

    public static IReadOnlyList<IKasaDeviceProfileDefinition> Definitions { get; } =
    [
        new KasaEp25ProfileDefinition(),
        new KasaHs105ProfileDefinition(),
        new KasaHs200ProfileDefinition(),
        new KasaHs210ProfileDefinition(),
        new KasaHs220ProfileDefinition(),
        new KasaHs300ProfileDefinition(),
        new KasaKp200ProfileDefinition(),
        new KasaKl130ProfileDefinition(),
        new KasaLb230ProfileDefinition()
    ];

    public static IKasaDeviceProfileDefinition Resolve(KasaSystemInfo systemInfo) =>
        Definitions.FirstOrDefault(definition => definition.Matches(systemInfo))
        ?? ResolveByShape(systemInfo)
        ?? UnknownDefinition;

    private static IKasaDeviceProfileDefinition? ResolveByShape(KasaSystemInfo systemInfo)
    {
        if (systemInfo.Children.Count > 1)
        {
            return systemInfo.Children.Count == 2
                ? Definitions.OfType<KasaKp200ProfileDefinition>().Single()
                : Definitions.OfType<KasaHs300ProfileDefinition>().Single();
        }

        if (systemInfo.LightState is not null)
        {
            return Definitions.OfType<KasaKl130ProfileDefinition>().Single();
        }

        if (systemInfo.RelayState is not null)
        {
            return Definitions.OfType<KasaHs105ProfileDefinition>().Single();
        }

        return null;
    }
}

public abstract class KasaDeviceProfileDefinition(
    string profileId,
    string displayName,
    KasaDeviceKind deviceKind,
    IReadOnlyList<string> modelPrefixes,
    IReadOnlyList<IKasaDeviceModule> modules) : IKasaDeviceProfileDefinition
{
    public string ProfileId { get; } = profileId;
    public string DisplayName { get; } = displayName;
    public KasaDeviceKind DeviceKind { get; } = deviceKind;
    public IReadOnlyList<string> ModelPrefixes { get; } = modelPrefixes;
    public IReadOnlyList<IKasaDeviceModule> Modules { get; } = modules;

    public virtual bool Matches(KasaSystemInfo systemInfo) =>
        ModelPrefixes.Any(prefix => systemInfo.Model?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true);

    public KasaDeviceProfile CreateRuntimeProfile(KasaSystemInfo systemInfo, KasaDeviceConfig? config, KasaEnergyReading? energy)
    {
        var capabilities = new HashSet<KasaCapability>();
        var metadataCapabilities = new HashSet<KasaMetadataCapability>()
        {
            KasaMetadataCapability.FirmwareInfo,
            KasaMetadataCapability.Diagnostics
        };
        var commandCapabilities = new HashSet<KasaCommandCapability>();

        foreach (var module in Modules)
        {
            foreach (var capability in module.Capabilities)
            {
                if (ShouldExposeCapability(capability, systemInfo, config, energy))
                {
                    capabilities.Add(capability);
                }
            }

            foreach (var commandCapability in module.CommandCapabilities)
            {
                commandCapabilities.Add(commandCapability);
            }
        }

        if (energy is not null || config?.Capabilities.Contains(KasaCapability.EnergyRealtime) == true)
        {
            capabilities.Add(KasaCapability.EnergyRealtime);
            metadataCapabilities.Add(KasaMetadataCapability.EnergyRealtime);
            if (energy?.EnergyKWh is not null || Modules.OfType<IKasaEnergyModule>().Any(module => module.HasTotalEnergy))
            {
                metadataCapabilities.Add(KasaMetadataCapability.EnergyTotal);
            }
        }

        if (config is not null)
        {
            capabilities.UnionWith(config.Capabilities);
            metadataCapabilities.UnionWith(config.MetadataCapabilities);
            commandCapabilities.UnionWith(config.CommandCapabilities);
        }

        capabilities.Add(KasaCapability.Diagnostics);
        return new KasaDeviceProfile(ResolveConfiguredKind(config), capabilities, metadataCapabilities, commandCapabilities);
    }

    private KasaDeviceKind ResolveConfiguredKind(KasaDeviceConfig? config) =>
        config?.DeviceKind is { } configuredKind && configuredKind != KasaDeviceKind.Auto
            ? configuredKind
            : DeviceKind;

    private static bool ShouldExposeCapability(KasaCapability capability, KasaSystemInfo systemInfo, KasaDeviceConfig? config, KasaEnergyReading? energy) =>
        capability switch
        {
            KasaCapability.SwitchState => systemInfo.RelayState is not null,
            KasaCapability.ChildOutlets => systemInfo.Children.Count > 0,
            KasaCapability.LightState => systemInfo.LightState is not null,
            KasaCapability.Dimming => systemInfo.LightState?.Brightness is not null || ModelMatches(config, systemInfo, "HS220"),
            KasaCapability.Color => systemInfo.LightState?.Hue is not null || systemInfo.LightState?.Saturation is not null || ModelMatches(config, systemInfo, "KL", "LB"),
            KasaCapability.VariableColorTemperature => systemInfo.LightState?.ColorTemperature is not null || ModelMatches(config, systemInfo, "KL", "LB"),
            KasaCapability.EnergyRealtime => energy is not null || config?.Capabilities.Contains(KasaCapability.EnergyRealtime) == true,
            _ => true
        };

    private static bool ModelMatches(KasaDeviceConfig? config, KasaSystemInfo systemInfo, params string[] values)
    {
        var model = systemInfo.Model ?? config?.ExpectedModel;
        return values.Any(value => model?.Contains(value, StringComparison.OrdinalIgnoreCase) == true);
    }
}

public sealed class KasaEp25ProfileDefinition() : KasaDeviceProfileDefinition(
    "ep25",
    "EP25 energy plug",
    KasaDeviceKind.Plug,
    ["EP25"],
    [KasaModules.Diagnostics, KasaModules.Switch, KasaModules.PlugEnergyWithHistory, KasaModules.LegacySchedule, KasaModules.LegacyLed]);

public sealed class KasaHs105ProfileDefinition() : KasaDeviceProfileDefinition(
    "hs105",
    "HS105 plug",
    KasaDeviceKind.Plug,
    ["HS105"],
    [KasaModules.Diagnostics, KasaModules.Switch, KasaModules.LegacySchedule, KasaModules.LegacyLed]);

public sealed class KasaHs200ProfileDefinition() : KasaDeviceProfileDefinition(
    "hs200",
    "HS200 wall switch",
    KasaDeviceKind.Switch,
    ["HS200"],
    [KasaModules.Diagnostics, KasaModules.Switch, KasaModules.LegacySchedule, KasaModules.LegacyLed]);

public sealed class KasaHs210ProfileDefinition() : KasaDeviceProfileDefinition(
    "hs210",
    "HS210 three-way switch",
    KasaDeviceKind.ThreeWaySwitch,
    ["HS210"],
    [KasaModules.Diagnostics, KasaModules.Switch, KasaModules.LegacySchedule, KasaModules.LegacyLed]);

public sealed class KasaHs220ProfileDefinition() : KasaDeviceProfileDefinition(
    "hs220",
    "HS220 dimmer switch",
    KasaDeviceKind.Dimmer,
    ["HS220"],
    [KasaModules.Diagnostics, KasaModules.Switch, KasaModules.LegacySchedule, KasaModules.LegacyLed, KasaModules.Dimmer]);

public sealed class KasaHs300ProfileDefinition() : KasaDeviceProfileDefinition(
    "hs300",
    "HS300 power strip",
    KasaDeviceKind.PowerStrip,
    ["HS300"],
    [KasaModules.Diagnostics, KasaModules.ChildOutlets(6), KasaModules.StripEnergyWithChildHistory, KasaModules.ChildScopedSchedule, KasaModules.LegacyLed]);

public sealed class KasaKp200ProfileDefinition() : KasaDeviceProfileDefinition(
    "kp200",
    "KP200 dual outlet",
    KasaDeviceKind.DualOutlet,
    ["KP200"],
    [KasaModules.Diagnostics, KasaModules.ChildOutlets(2), KasaModules.ChildScopedSchedule, KasaModules.LegacyLed]);

public sealed class KasaKl130ProfileDefinition() : KasaDeviceProfileDefinition(
    "kl130",
    "KL130 RGB bulb",
    KasaDeviceKind.Bulb,
    ["KL130"],
    [KasaModules.Diagnostics, KasaModules.ColorBulbLight, KasaModules.BulbPowerOnlyEnergy, KasaModules.BulbSchedule]);

public sealed class KasaLb230ProfileDefinition() : KasaDeviceProfileDefinition(
    "lb230",
    "LB230 RGB bulb",
    KasaDeviceKind.Bulb,
    ["LB230"],
    [KasaModules.Diagnostics, KasaModules.ColorBulbLight, KasaModules.BulbPowerOnlyEnergy, KasaModules.BulbSchedule]);

public sealed class KasaUnknownProfileDefinition() : KasaDeviceProfileDefinition(
    "unknown",
    "Unknown Kasa device",
    KasaDeviceKind.Unknown,
    [],
    [KasaModules.Diagnostics]);

internal static class KasaModules
{
    public static IKasaDiagnosticsModule Diagnostics { get; } = new KasaDiagnosticsModule();
    public static IKasaSwitchModule Switch { get; } = new KasaSwitchModule();
    public static IKasaScheduleModule LegacySchedule { get; } = new KasaScheduleModule("legacySchedule", "Legacy schedule/timer/away", "device", false, true, true);
    public static IKasaScheduleModule ChildScopedSchedule { get; } = new KasaScheduleModule("childSchedule", "Child schedule/timer/away", "child", true, true, true);
    public static IKasaScheduleModule BulbSchedule { get; } = new KasaScheduleModule("bulbSchedule", "Bulb schedule", "device", false, false, false);
    public static IKasaDeviceModule LegacyLed { get; } = new KasaBasicModule("led", "LED state", "device", KasaSafetyClass.TelemetryOnly, Set(KasaCapability.LedState), Set(KasaMetadataCapability.LedRead), Set<KasaCommandCapability>());
    public static IKasaEnergyModule PlugEnergyWithHistory { get; } = new KasaEnergyModule("energy", "Energy meter", "device", true, true, true, true, true);
    public static IKasaEnergyModule StripEnergyWithChildHistory { get; } = new KasaEnergyModule("childEnergy", "Child outlet energy meters", "child", true, true, true, true, true);
    public static IKasaEnergyModule BulbPowerOnlyEnergy { get; } = new KasaEnergyModule("bulbPower", "Bulb power meter", "device", true, false, false, false, false);
    public static IKasaLightModule ColorBulbLight { get; } = new KasaLightModule();
    public static IKasaDimmerModule Dimmer { get; } = new KasaDimmerModule();

    public static IKasaChildOutletModule ChildOutlets(int expectedOutletCount) => new KasaChildOutletModule(expectedOutletCount);

    public static IReadOnlySet<T> Set<T>(params T[] values)
        where T : struct, Enum => new HashSet<T>(values);
}

internal abstract record KasaDeviceModule(
    string ModuleId,
    string DisplayName,
    string Scope,
    KasaSafetyClass SafetyClass,
    IReadOnlySet<KasaCapability> Capabilities,
    IReadOnlySet<KasaMetadataCapability> MetadataCapabilities,
    IReadOnlySet<KasaCommandCapability> CommandCapabilities) : IKasaDeviceModule;

internal sealed record KasaBasicModule(
    string ModuleId,
    string DisplayName,
    string Scope,
    KasaSafetyClass SafetyClass,
    IReadOnlySet<KasaCapability> Capabilities,
    IReadOnlySet<KasaMetadataCapability> MetadataCapabilities,
    IReadOnlySet<KasaCommandCapability> CommandCapabilities) : KasaDeviceModule(ModuleId, DisplayName, Scope, SafetyClass, Capabilities, MetadataCapabilities, CommandCapabilities);

internal sealed record KasaDiagnosticsModule() : KasaDeviceModule(
    "diagnostics",
    "Diagnostics",
    "device",
    KasaSafetyClass.TelemetryOnly,
    KasaModules.Set(KasaCapability.Diagnostics),
    KasaModules.Set(KasaMetadataCapability.FirmwareInfo, KasaMetadataCapability.Diagnostics),
    KasaModules.Set<KasaCommandCapability>()), IKasaDiagnosticsModule;

internal sealed record KasaSwitchModule() : KasaDeviceModule(
    "switch",
    "Switch state",
    "device",
    KasaSafetyClass.HighRiskCommand,
    KasaModules.Set(KasaCapability.SwitchState),
    KasaModules.Set<KasaMetadataCapability>(),
    KasaModules.Set(KasaCommandCapability.SwitchPower)), IKasaSwitchModule;

internal sealed record KasaChildOutletModule(int? ExpectedOutletCount) : KasaDeviceModule(
    "childOutlets",
    "Child outlets",
    "child",
    KasaSafetyClass.HighRiskCommand,
    KasaModules.Set(KasaCapability.ChildOutlets),
    KasaModules.Set<KasaMetadataCapability>(),
    KasaModules.Set(KasaCommandCapability.SwitchPower)), IKasaChildOutletModule;

internal sealed record KasaEnergyModule(
    string ModuleId,
    string DisplayName,
    string Scope,
    bool HasRealtimePower,
    bool HasVoltage,
    bool HasCurrent,
    bool HasTotalEnergy,
    bool HasHistory) : KasaDeviceModule(
        ModuleId,
        DisplayName,
        Scope,
        KasaSafetyClass.TelemetryOnly,
        KasaModules.Set(KasaCapability.EnergyRealtime),
        HasTotalEnergy || HasHistory ? KasaModules.Set(KasaMetadataCapability.EnergyRealtime, KasaMetadataCapability.EnergyTotal) : KasaModules.Set(KasaMetadataCapability.EnergyRealtime),
        KasaModules.Set<KasaCommandCapability>()), IKasaEnergyModule;

internal sealed record KasaScheduleModule(
    string ModuleId,
    string DisplayName,
    string Scope,
    bool IsChildScoped,
    bool SupportsCountdown,
    bool SupportsAwayMode) : KasaDeviceModule(
        ModuleId,
        DisplayName,
        Scope,
        KasaSafetyClass.HighRiskCommand,
        KasaModules.Set(KasaCapability.ScheduleMetadata),
        BuildScheduleMetadataCapabilities(SupportsCountdown, SupportsAwayMode),
        KasaModules.Set<KasaCommandCapability>()), IKasaScheduleModule
{
    private static IReadOnlySet<KasaMetadataCapability> BuildScheduleMetadataCapabilities(bool supportsCountdown, bool supportsAwayMode)
    {
        var capabilities = new HashSet<KasaMetadataCapability> { KasaMetadataCapability.ScheduleRead };
        if (supportsCountdown)
        {
            capabilities.Add(KasaMetadataCapability.CountdownRead);
        }

        if (supportsAwayMode)
        {
            capabilities.Add(KasaMetadataCapability.AwayModeRead);
        }

        return capabilities;
    }
}

internal sealed record KasaLightModule() : KasaDeviceModule(
    "light",
    "Color bulb light",
    "device",
    KasaSafetyClass.HighRiskCommand,
    KasaModules.Set(KasaCapability.LightState, KasaCapability.Dimming, KasaCapability.Color, KasaCapability.VariableColorTemperature),
    KasaModules.Set(KasaMetadataCapability.BulbLightRead),
    KasaModules.Set(KasaCommandCapability.SwitchPower, KasaCommandCapability.DimLevel, KasaCommandCapability.LightColor, KasaCommandCapability.LightColorTemperature)), IKasaLightModule
{
    public bool SupportsDimming => true;
    public bool SupportsColor => true;
    public bool SupportsColorTemperature => true;
    public bool HasPreferredStates => true;
}

internal sealed record KasaDimmerModule() : KasaDeviceModule(
    "dimmer",
    "Dimmer parameters",
    "device",
    KasaSafetyClass.HighRiskCommand,
    KasaModules.Set(KasaCapability.Dimming),
    KasaModules.Set(KasaMetadataCapability.DimmerRead),
    KasaModules.Set(KasaCommandCapability.DimLevel)), IKasaDimmerModule
{
    public bool HasDefaultBehavior => true;
    public bool HasCalibrationParameters => true;
}

