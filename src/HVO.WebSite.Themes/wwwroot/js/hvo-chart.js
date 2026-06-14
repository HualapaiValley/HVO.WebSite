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

window.hvoChart.render = function (chartId, config) {
    const canvas = document.getElementById(chartId);
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const existing = window.hvoChart._instances && window.hvoChart._instances[chartId];
    if (existing) {
        existing.destroy();
    }

    const colors = window.hvoChart.getThemeColors();

    const defaultScales = config.options && config.options.scales;
    if (defaultScales) {
        if (defaultScales.x) {
            defaultScales.x.grid = { color: colors.gridColor };
            defaultScales.x.ticks = { color: colors.labelColor };
        }
        if (defaultScales.y) {
            defaultScales.y.grid = { color: colors.gridColor };
            defaultScales.y.ticks = { color: colors.labelColor };
        }
    }

    if (config.options && config.options.plugins) {
        config.options.plugins.legend = config.options.plugins.legend || {};
        config.options.plugins.legend.labels = config.options.plugins.legend.labels || {};
        config.options.plugins.legend.labels.color = colors.labelColor;
    }

    const instance = new Chart(ctx, config);

    window.hvoChart._instances = window.hvoChart._instances || {};
    window.hvoChart._instances[chartId] = instance;

    return instance;
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
