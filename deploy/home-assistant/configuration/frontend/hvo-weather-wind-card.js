const DIRECTIONS = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

class HvoWeatherWindCard extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: "open" });
        this._config = null;
        this._hass = null;
    }

    setConfig(config) {
        if (!config.freshness_entity || !config.direction_entity || !config.speed_entity) {
            throw new Error("freshness_entity, direction_entity, and speed_entity are required");
        }

        this._config = config;
        this.render();
    }

    set hass(value) {
        this._hass = value;
        this.render();
    }

    getCardSize() {
        return 4;
    }

    state(entityId) {
        return entityId ? this._hass?.states?.[entityId] : undefined;
    }

    numericState(entityId) {
        const state = this.state(entityId);
        if (!state || state.state === "unknown" || state.state === "unavailable") {
            return null;
        }

        const value = Number(state.state);
        return Number.isFinite(value) ? value : null;
    }

    cardinal(degrees) {
        if (degrees === null) {
            return "--";
        }

        const normalized = ((degrees % 360) + 360) % 360;
        return DIRECTIONS[Math.round(normalized / 45) % DIRECTIONS.length];
    }

    freshnessState() {
        const state = this.state(this._config.freshness_entity)?.state?.toLowerCase();
        return state === "live"
            ? { label: "Current", className: "current" }
            : state === "stale"
                ? { label: "Stale", className: "stale" }
                : state === "error"
                    ? { label: "Error", className: "error" }
                    : state === "waiting" || state === "unknown"
                        ? { label: "Waiting", className: "waiting" }
                        : { label: "Unavailable", className: "unavailable" };
    }

    observationAge(state) {
        const updatedAt = state?.last_updated ? Date.parse(state.last_updated) : Number.NaN;
        if (!Number.isFinite(updatedAt)) {
            return "Wind observation time unavailable";
        }

        const seconds = Math.max(0, Math.floor((Date.now() - updatedAt) / 1000));
        if (seconds < 60) {
            return `Wind observation updated ${seconds}s ago`;
        }

        const minutes = Math.floor(seconds / 60);
        return `Wind observation updated ${minutes}m ago`;
    }

    render() {
        if (!this._config || !this._hass) {
            return;
        }

        const directionState = this.state(this._config.direction_entity);
        const direction = this.numericState(this._config.direction_entity);
        const speed = this.numericState(this._config.speed_entity);
        const average2 = this.numericState(this._config.average_2_entity);
        const average10 = this.numericState(this._config.average_10_entity);
        const gust = this.numericState(this._config.gust_entity);
        const gustDirection = this.numericState(this._config.gust_direction_entity);
        const freshness = this.freshnessState();
        const readingsUnavailable = direction === null || speed === null;
        const normalizedDirection = direction === null ? 0 : ((direction % 360) + 360) % 360;

        this.shadowRoot.innerHTML = `
            <style>
                :host { display: block; min-width: 0; }
                ha-card {
                    display: block;
                    padding: 18px;
                    color: var(--primary-text-color);
                    background: var(--ha-card-background, var(--card-background-color));
                    overflow: hidden;
                }
                .header { display: flex; justify-content: space-between; gap: 12px; align-items: baseline; }
                h2 { margin: 0; font-size: 1.25rem; font-weight: 500; }
                .status { font-size: 0.78rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.08em; }
                .current { color: var(--success-color); }
                .stale { color: var(--warning-color); }
                .waiting { color: var(--warning-color); }
                .error { color: var(--error-color); }
                .unavailable { color: var(--error-color); }
                .content { display: grid; grid-template-columns: minmax(180px, 1fr) minmax(190px, 1fr); gap: 20px; align-items: center; margin-top: 14px; }
                .compass { position: relative; width: min(100%, 250px); aspect-ratio: 1; margin: auto; border: 2px solid var(--divider-color); border-radius: 50%; background: var(--secondary-background-color); }
                .ring { position: absolute; inset: 13%; border: 1px solid var(--divider-color); border-radius: 50%; }
                .axis { position: absolute; inset: 8%; color: var(--secondary-text-color); font-size: 0.75rem; font-weight: 700; }
                .axis span { position: absolute; transform: translate(-50%, -50%); }
                .north { left: 50%; top: 0; }
                .east { left: 100%; top: 50%; }
                .south { left: 50%; top: 100%; }
                .west { left: 0; top: 50%; }
                .needle { position: absolute; inset: 10%; transform: rotate(var(--direction)); transition: transform 180ms ease-out; }
                .needle::before { content: ""; position: absolute; left: 50%; top: 2%; transform: translateX(-50%); border-left: 9px solid transparent; border-right: 9px solid transparent; border-bottom: 80px solid var(--primary-color); }
                .hub { position: absolute; width: 16px; height: 16px; left: 50%; top: 50%; transform: translate(-50%, -50%); border-radius: 50%; background: var(--primary-color); border: 3px solid var(--card-background-color); }
                .readout { position: absolute; left: 50%; top: 58%; transform: translateX(-50%); text-align: center; white-space: nowrap; }
                .readout strong { display: block; font-size: 1.4rem; }
                .readout span { color: var(--secondary-text-color); font-size: 0.8rem; }
                dl { display: grid; grid-template-columns: 1fr auto; gap: 11px 16px; margin: 0; min-width: 0; }
                dt { color: var(--secondary-text-color); }
                dd { margin: 0; font-weight: 600; text-align: right; }
                .detail { margin: 14px 0 0; color: var(--secondary-text-color); font-size: 0.78rem; }
                .detail strong { color: var(--error-color); }
                @media (max-width: 520px) {
                    ha-card { padding: 14px; }
                    .content { grid-template-columns: 1fr; }
                    .compass { width: min(100%, 220px); }
                }
            </style>
            <ha-card>
                <div class="header">
                    <h2>${this._config.title ?? "Wind"}</h2>
                    <span class="status ${freshness.className}" data-testid="wind-status">${freshness.label}</span>
                </div>
                <div class="content">
                    <div class="compass" aria-label="Wind direction ${direction === null ? "unavailable" : `${Math.round(normalizedDirection)} degrees ${this.cardinal(direction)}`}" data-testid="wind-compass">
                        <div class="ring"></div>
                        <div class="axis"><span class="north">N</span><span class="east">E</span><span class="south">S</span><span class="west">W</span></div>
                        <div class="needle" style="--direction: ${normalizedDirection}deg" data-testid="wind-needle"></div>
                        <div class="hub"></div>
                        <div class="readout"><strong data-testid="wind-cardinal">${this.cardinal(direction)}</strong><span>${direction === null ? "--" : `${Math.round(normalizedDirection)}°`}</span></div>
                    </div>
                    <dl>
                        <dt>Current speed</dt><dd>${this.format(speed, "mph")}</dd>
                        <dt>2-minute average</dt><dd>${this.format(average2, "mph", 1)}</dd>
                        <dt>10-minute average</dt><dd>${this.format(average10, "mph", 1)}</dd>
                        <dt>10-minute gust</dt><dd>${this.format(gust, "mph")}</dd>
                        <dt>Gust direction</dt><dd>${gustDirection === null ? "--" : `${Math.round(gustDirection)}° ${this.cardinal(gustDirection)}`}</dd>
                    </dl>
                </div>
                <p class="detail" data-testid="wind-observation-age">${this.observationAge(directionState)}${readingsUnavailable ? " · <strong>Wind readings unavailable</strong>" : ""}</p>
            </ha-card>`;
    }

    format(value, unit, digits = 0) {
        return value === null ? "--" : `${value.toFixed(digits)} ${unit}`;
    }
}

if (!customElements.get("hvo-weather-wind-card")) {
    customElements.define("hvo-weather-wind-card", HvoWeatherWindCard);
}

window.customCards = window.customCards || [];
window.customCards.push({
    type: "hvo-weather-wind-card",
    name: "HVO Weather Wind Card",
    description: "Local Davis wind compass with freshness state"
});
