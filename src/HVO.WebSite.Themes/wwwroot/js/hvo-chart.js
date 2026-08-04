window.hvoChart = window.hvoChart || {};

window.hvoChart.getThemeColors = function () {
    const style = getComputedStyle(document.documentElement);
    return {
        // --shell-chart-grid-color
        gridColor: style.getPropertyValue('--shell-chart-grid-color').trim() || 'rgba(126, 157, 196, 0.12)',
        // --shell-chart-axis-color
        axisColor: style.getPropertyValue('--shell-chart-axis-color').trim() || 'rgba(126, 157, 196, 0.18)',
        // --shell-chart-label-color
        labelColor: style.getPropertyValue('--shell-chart-label-color').trim() || '#8da4c7',
        // --shell-chart-marker-fill
        markerFill: style.getPropertyValue('--shell-chart-marker-fill').trim() || '#ebf5ff',
        // --shell-chart-empty-background
        emptyBackground: style.getPropertyValue('--shell-chart-empty-background').trim() || 'rgba(8, 18, 30, 0.28)',
        // --shell-chart-empty-border
        emptyBorder: style.getPropertyValue('--shell-chart-empty-border').trim() || 'rgba(126, 157, 196, 0.18)'
    };
};

/**
 * Resolve CSS var() references in a string to their computed values.
 * Canvas 2D cannot resolve CSS custom properties, so dataset defaults
 * like "var(--shell-chart-label-color)" must be resolved to hex/rgba
 * before passing to Chart.js.
 *
 * Resolves against @param {HTMLElement} [element] so that theme overrides
 * from ancestor classes (e.g. .shell-theme-light) are honoured. Falls back
 * to document.documentElement when no element is provided.
 */
window.hvoChart.resolveCssVar = function (value, element) {
    if (typeof value !== 'string' || !value.startsWith('var(')) return value;
    var style = getComputedStyle(element || document.documentElement);
    // Extract the property name from var(--xxx) or var(--xxx, fallback)
    var match = value.match(/var\((--[\w-]+)(?:\s*,\s*([^)]+))?\)/);
    if (!match) return value;
    var propName = match[1];
    var fallback = match[2] || '';
    var resolved = style.getPropertyValue(propName).trim();
    return resolved || fallback || value;
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

        // Resolve CSS var() defaults in dataset colors to computed values.
        // Canvas 2D cannot resolve CSS custom properties, so dataset fallbacks
        // like "var(--shell-chart-label-color)" must be resolved here.
        // Resolve against the canvas element so theme overrides from ancestor
        // classes (e.g. .shell-theme-light) are honoured.
        if (cleanConfig.data && cleanConfig.data.datasets) {
            cleanConfig.data.datasets.forEach(function (ds) {
                if (ds.borderColor) ds.borderColor = window.hvoChart.resolveCssVar(ds.borderColor, canvas);
                if (ds.backgroundColor) ds.backgroundColor = window.hvoChart.resolveCssVar(ds.backgroundColor, canvas);
            });
        }

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

            const showFahrenheitAxis = defaultScales.fahrenheitAxis;
            delete defaultScales.fahrenheitAxis;
            if (showFahrenheitAxis && defaultScales.y) {
                const values = (cleanConfig.data && cleanConfig.data.datasets || [])
                    .flatMap(ds => Array.isArray(ds.data) ? ds.data : [])
                    .filter(value => typeof value === 'number' && Number.isFinite(value));
                let minimum = defaultScales.y.suggestedMin;
                let maximum = defaultScales.y.suggestedMax;
                if (minimum === undefined || maximum === undefined) {
                    let dataMinimum = Number.POSITIVE_INFINITY;
                    let dataMaximum = Number.NEGATIVE_INFINITY;
                    values.forEach(value => {
                        if (value < dataMinimum) dataMinimum = value;
                        if (value > dataMaximum) dataMaximum = value;
                    });
                    if (minimum === undefined) minimum = values.length ? dataMinimum : 0;
                    if (maximum === undefined) maximum = values.length ? dataMaximum : 1;
                }
                if (minimum === maximum) {
                    const padding = Math.max(Math.abs(minimum) * 0.05, 1);
                    minimum -= padding;
                    maximum += padding;
                }
                defaultScales.y.min = minimum;
                defaultScales.y.max = maximum;
                defaultScales.yF = {
                    type: 'linear',
                    position: 'right',
                    min: minimum,
                    max: maximum,
                    grid: { drawOnChartArea: false },
                    title: { display: true, text: 'Fahrenheit' },
                    ticks: {
                        color: colors.labelColor,
                        callback: value => `${((Number(value) * 9 / 5) + 32).toFixed(0)} °F`
                    }
                };
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
        if (instance.options.scales.yF) {
            instance.options.scales.yF.ticks.color = colors.labelColor;
            if (instance.options.scales.yF.title) {
                instance.options.scales.yF.title.color = colors.labelColor;
            }
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
