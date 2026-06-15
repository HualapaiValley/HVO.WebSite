window.hvoChart = window.hvoChart || {};

window.hvoChart.getThemeColors = function () {
    const style = getComputedStyle(document.documentElement);
    return {
        gridColor: style.getPropertyValue('--shell-chart-grid-color').trim() || 'rgba(126, 157, 196, 0.12)',
        axisColor: style.getPropertyValue('--shell-chart-axis-color').trim() || 'rgba(126, 157, 196, 0.18)',
        labelColor: style.getPropertyValue('--shell-chart-label-color').trim() || '#8da4c7',
        markerFill: style.getPropertyValue('--shell-chart-marker-fill').trim() || '#ebf5ff',
        emptyBackground: style.getPropertyValue('--shell-chart-empty-background').trim() || 'rgba(8, 18, 30, 0.28)',
        emptyBorder: style.getPropertyValue('--shell-chart-empty-border').trim() || 'rgba(126, 157, 196, 0.18)'
    };
};

/**
 * Recursively remove null and undefined properties from a config object.
 * Chart.js 4.x treats null as an invalid value for many options (e.g. title,
 * suggestedMin/Max) but treats absent/undefined keys as "use default".
 * C# anonymous-type serialisation always emits null for nullable properties,
 * so we strip them here before passing to new Chart().
 */
window.hvoChart.stripNulls = function stripNulls(obj) {
    if (obj === null || obj === undefined) return undefined;
    if (Array.isArray(obj)) {
        // Preserve null entries inside data arrays — Chart.js uses them for gaps.
        return obj.map(function (item) {
            return (item !== null && typeof item === 'object') ? stripNulls(item) : item;
        });
    }
    if (typeof obj === 'object') {
        const out = {};
        for (const key of Object.keys(obj)) {
            const val = obj[key];
            if (val === null || val === undefined) continue;
            out[key] = stripNulls(val);
        }
        return out;
    }
    return obj;
};

window.hvoChart.render = function (chartId, config) {
    try {
        const canvas = document.getElementById(chartId);
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        const existing = window.hvoChart._instances && window.hvoChart._instances[chartId];
        if (existing) {
            existing.destroy();
        }

        const colors = window.hvoChart.getThemeColors();

        // Strip nulls first so Chart.js only sees valid or absent properties.
        const cleanConfig = window.hvoChart.stripNulls(config);

        // Inject theme colours into scale axes.
        const defaultScales = cleanConfig.options && cleanConfig.options.scales;
        if (defaultScales) {
            if (defaultScales.x) {
                defaultScales.x.grid = { color: colors.gridColor };
                const callerTicks = defaultScales.x.ticks || {};
                defaultScales.x.ticks = Object.assign(
                    { maxTicksLimit: 7, autoSkip: true, maxRotation: 0 },
                    callerTicks,
                    { color: colors.labelColor }
                );
            }
            if (defaultScales.y) {
                defaultScales.y.grid = { color: colors.gridColor };
                const callerYTicks = defaultScales.y.ticks || {};
                defaultScales.y.ticks = Object.assign(callerYTicks, { color: colors.labelColor });
            }
        }

        if (cleanConfig.options && cleanConfig.options.plugins) {
            cleanConfig.options.plugins.legend = cleanConfig.options.plugins.legend || {};
            cleanConfig.options.plugins.legend.labels = cleanConfig.options.plugins.legend.labels || {};
            cleanConfig.options.plugins.legend.labels.color = colors.labelColor;
        }

        const instance = new Chart(ctx, cleanConfig);

        window.hvoChart._instances = window.hvoChart._instances || {};
        window.hvoChart._instances[chartId] = instance;

        return instance;
    } catch (e) {
        console.error('[HvoChart] render failed for "' + chartId + '":', e);
    }
};

window.hvoChart.applyTheme = function (chartId) {
    const instance = window.hvoChart._instances && window.hvoChart._instances[chartId];
    if (!instance) return;

    const colors = window.hvoChart.getThemeColors();

    if (instance.options.scales) {
        if (instance.options.scales.x) {
            instance.options.scales.x.grid.color = colors.gridColor;
            instance.options.scales.x.ticks.color = colors.labelColor;
        }
        if (instance.options.scales.y) {
            instance.options.scales.y.grid.color = colors.gridColor;
            instance.options.scales.y.ticks.color = colors.labelColor;
        }
    }

    if (instance.options.plugins && instance.options.plugins.legend) {
        instance.options.plugins.legend.labels.color = colors.labelColor;
    }

    instance.update();
};

window.hvoChart.destroy = function (chartId) {
    const instance = window.hvoChart._instances && window.hvoChart._instances[chartId];
    if (instance) {
        instance.destroy();
        delete window.hvoChart._instances[chartId];
    }
};
