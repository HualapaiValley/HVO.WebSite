window.hvoChart = window.hvoChart || {};

window.hvoChart.getThemeColors = function (canvas) {
    const style = getComputedStyle(canvas || document.documentElement);
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
    if (Array.isArray(value)) return value.map(color => window.hvoChart.resolveCssVar(color, element));
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
        // Replacements own a new chart and observer, including after failed renders.
        window.hvoChart.destroy(chartId);
        const canvas = document.getElementById(chartId);
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        // Strip nulls first so Chart.js only sees valid or absent properties.
        const cleanConfig = window.hvoChart.stripNulls(config);
        // Keep the original scalar/array specifications outside Chart.js's mutable
        // config. Resolving them in place would lose the vars after the first theme.
        const datasetColors = (cleanConfig.data?.datasets || []).map(dataset => ({
            borderColor: dataset.borderColor,
            backgroundColor: dataset.backgroundColor
        }));

        // Inject theme colours into scale axes.
        const defaultScales = cleanConfig.options && cleanConfig.options.scales;
        if (defaultScales) {
            if (defaultScales.x) {
                const callerTicks = defaultScales.x.ticks || {};
                defaultScales.x.ticks = Object.assign(
                    { maxTicksLimit: 7, autoSkip: true, maxRotation: 0 },
                    callerTicks
                );
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
                        callback: value => `${((Number(value) * 9 / 5) + 32).toFixed(0)} °F`
                    }
                };
            }
        }

        const theme = window.hvoChart.readTheme(canvas, datasetColors);
        cleanConfig.options = cleanConfig.options || {};
        if (cleanConfig.type === 'polarArea' && !cleanConfig.options.scales) {
            cleanConfig.options.scales = { r: {} };
        }
        window.hvoChart.setTheme(cleanConfig, theme);
        const instance = new Chart(ctx, cleanConfig);

        window.hvoChart._instances = window.hvoChart._instances || {};
        window.hvoChart._instances[chartId] = instance;
        window.hvoChart._themes = window.hvoChart._themes || {};
        const state = { canvas, datasetColors, signature: JSON.stringify(theme) };
        window.hvoChart._themes[chartId] = state;
        state.observer = new MutationObserver(() => {
            if (window.hvoChart._themes[chartId] === state) window.hvoChart.applyTheme(chartId);
        });
        // Shell theme classes are scoped to layouts, not necessarily the HTML root.
        // Attribute-only observation avoids watching chart/page child mutations.
        for (let element = canvas; element; element = element.parentElement) {
            state.observer.observe(element, { attributes: true, attributeFilter: ['class', 'style'] });
        }

        return instance;
    } catch (e) {
        try {
            window.hvoChart.destroy(chartId);
            // Chart.js can register a partial instance before a constructor error.
            const canvas = document.getElementById(chartId);
            const partial = canvas && typeof Chart !== 'undefined' && Chart.getChart?.(canvas);
            if (partial) partial.destroy();
        } catch (cleanupError) {
            console.error('[HvoChart] failed-render cleanup failed for "' + chartId + '":', cleanupError);
        }
        console.error('[HvoChart] render failed for "' + chartId + '":', e);
    }
};

window.hvoChart.readTheme = function (canvas, datasetColors) {
    return {
        colors: window.hvoChart.getThemeColors(canvas),
        datasets: datasetColors.map(specification => ({
            borderColor: window.hvoChart.resolveCssVar(specification.borderColor, canvas),
            backgroundColor: window.hvoChart.resolveCssVar(specification.backgroundColor, canvas)
        }))
    };
};

window.hvoChart.setTheme = function (chart, theme) {
    const colors = theme.colors;
    for (const scale of Object.values(chart.options.scales || {})) {
        if (!scale) continue;
        scale.grid = scale.grid || {};
        scale.grid.color = colors.gridColor;
        scale.border = scale.border || {};
        scale.border.color = colors.axisColor;
        scale.ticks = scale.ticks || {};
        scale.ticks.color = colors.labelColor;
        if (scale.title) scale.title.color = colors.labelColor;
        if (scale.pointLabels) scale.pointLabels.color = colors.labelColor;
    }
    chart.options.plugins = chart.options.plugins || {};
    const plugins = chart.options.plugins;
    if (plugins.legend !== false) {
        plugins.legend = plugins.legend || {};
        plugins.legend.labels = plugins.legend.labels || {};
        plugins.legend.labels.color = colors.labelColor;
    }
    if (plugins.title) plugins.title.color = colors.labelColor;
    (chart.data?.datasets || []).forEach((dataset, index) => {
        for (const [property, value] of Object.entries(theme.datasets[index] || {})) {
            if (value !== undefined) dataset[property] = value;
        }
    });
};

window.hvoChart.applyTheme = function (chartId) {
    const instance = window.hvoChart._instances?.[chartId];
    const state = window.hvoChart._themes?.[chartId];
    if (!instance || !state) return;
    try {
        const theme = window.hvoChart.readTheme(state.canvas, state.datasetColors);
        const signature = JSON.stringify(theme);
        if (signature === state.signature) return;
        // Mutate the caller configuration, not Chart.js's resolved options proxy.
        // Enumerating that proxy can pass symbol keys into scriptable resolvers.
        window.hvoChart.setTheme(instance.config, theme);
        instance.update('none');
        state.signature = signature;
    } catch (e) {
        console.error('[HvoChart] theme update failed for "' + chartId + '":', e);
    }
};

window.hvoChart.destroy = function (chartId) {
    const state = window.hvoChart._themes?.[chartId];
    state?.observer?.disconnect();
    if (window.hvoChart._themes) delete window.hvoChart._themes[chartId];
    const instance = window.hvoChart._instances?.[chartId];
    if (window.hvoChart._instances) delete window.hvoChart._instances[chartId];
    if (instance) {
        instance.destroy();
    }
};
