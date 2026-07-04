namespace HVO.Edge.Contracts;

/// <summary>
/// Centralized registry of all edge payload type identifiers used across gateways.
/// Each constant follows the CloudEvents reverse-DNS naming convention:
/// <c>com.hvo.{domain}.{event}.v{version}</c>
/// </summary>
/// <remarks>
/// Gateway-specific code should reference these constants rather than defining
/// duplicate string literals. This ensures consistency between writers, forwarders,
/// and the website API receiver.
///
/// The legacy dot-notation names (e.g. "weather.raw") remain for backward
/// compatibility in existing outbox records and are aliased by the
/// <see cref="Legacy"/> sub-class.
/// </remarks>
public static class EdgePayloadTypes
{
    // ── Weather (Davis Vantage Pro2) ────────────────────────────────────
    public const string WeatherRaw = "com.hvo.weather.raw.v1";
    public const string WeatherArchive = "com.hvo.weather.archive.v1";
    public const string WeatherConfig = "com.hvo.weather.config.v1";

    // ── BMS (JK BMS) ────────────────────────────────────────────────────
    public const string BmsReading = "com.hvo.bms.reading.v1";
    public const string BmsConfig = "com.hvo.bms.config.v1";
    public const string BmsDeviceInfo = "com.hvo.bms.device-info.v1";

    // ── Power (Victron SmartShunt & SolarAssistant) ─────────────────────
    public const string PowerReading = "com.hvo.power.reading.v1";
    public const string PowerDeviceInventory = "com.hvo.power.device-inventory.v1";
    public const string PowerConfiguration = "com.hvo.power.configuration.v1";
    public const string PowerEnergy = "com.hvo.power.energy.v1";
    public const string PowerInverterDetail = "com.hvo.power.inverter-detail.v1";

    // ── Plug (TP-Link Kasa) ─────────────────────────────────────────────
    public const string PlugReading = "com.hvo.plug.reading.v1";
    public const string PlugState = "com.hvo.plug.state.v1";
    public const string KasaEnergy = "com.hvo.kasa.energy.v1";
    public const string KasaInventory = "com.hvo.kasa.inventory.v1";

    // ── SmartShunt (Victron) ─────────────────────────────────────────────
    public const string SmartShuntReading = "com.hvo.smartshunt.reading.v1";

    // ── Gateway status (all gateways) ────────────────────────────────────
    public const string GatewayStatus = "com.hvo.gateway.status.v1";

    /// <summary>
    /// Legacy payload type identifiers used in existing outbox records.
    /// New code should prefer the CloudEvents-style constants above.
    /// These remain for backward compatibility with stored data and
    /// as the value stored in <see cref="EdgeOutboxRecord.PayloadType"/>
    /// until a migration is performed.
    /// </summary>
    public static class Legacy
    {
        public const string WeatherRaw = "weather.raw";
        public const string WeatherArchive = "weather.archive";
        public const string WeatherConfig = "weather.config";

        public const string BmsReading = "bms.reading";
        public const string BmsConfig = "bms.config";
        public const string BmsDeviceInfo = "bms.device-info";

        public const string PowerReading = "power.reading";
        public const string PowerDeviceInventory = "power.device-inventory";
        public const string PowerConfiguration = "power.configuration";
        public const string PowerEnergy = "power.energy";
        public const string PowerInverterDetail = "power.inverter-detail";

        public const string PlugReading = "plug.reading";
        public const string PlugState = "plug.state";

        public const string KasaEnergy = "kasa.energy";
        public const string KasaInventory = "kasa.inventory";

        public const string SmartShuntReading = "smartshunt.reading";

        public const string GatewayStatus = "gateway.status";
    }

    /// <summary>
    /// Maps a legacy payload type to its CloudEvents equivalent.
    /// Returns the input unchanged if it is already a CloudEvents type or unknown.
    /// </summary>
    public static string ToCloudEventType(string legacyType) => legacyType switch
    {
        Legacy.WeatherRaw => WeatherRaw,
        Legacy.WeatherArchive => WeatherArchive,
        Legacy.WeatherConfig => WeatherConfig,
        Legacy.BmsReading => BmsReading,
        Legacy.BmsConfig => BmsConfig,
        Legacy.BmsDeviceInfo => BmsDeviceInfo,
        Legacy.PowerReading => PowerReading,
        Legacy.PowerDeviceInventory => PowerDeviceInventory,
        Legacy.PowerConfiguration => PowerConfiguration,
        Legacy.PowerEnergy => PowerEnergy,
        Legacy.PowerInverterDetail => PowerInverterDetail,
        Legacy.PlugReading => PlugReading,
        Legacy.PlugState => PlugState,
        Legacy.KasaEnergy => KasaEnergy,
        Legacy.KasaInventory => KasaInventory,
        Legacy.SmartShuntReading => SmartShuntReading,
        Legacy.GatewayStatus => GatewayStatus,
        _ => legacyType,
    };
}
