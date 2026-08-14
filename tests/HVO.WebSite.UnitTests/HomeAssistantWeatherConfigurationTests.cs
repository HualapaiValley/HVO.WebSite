using FluentAssertions;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class HomeAssistantWeatherConfigurationTests
{
    private static string ConfigurationRoot =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "HomeAssistant", "Configuration");

    [TestMethod]
    public void WeatherDashboard_IsRegisteredAsCanonicalOfflineDashboard()
    {
        var lovelace = File.ReadAllText(Path.Combine(ConfigurationRoot, "lovelace.yaml"));
        var operations = File.ReadAllText(Path.Combine(ConfigurationRoot, "dashboards", "hvo-operations.yaml"));

        lovelace.Should().Contain("resource_mode: yaml")
            .And.Contain("hvo-weather:")
            .And.Contain("filename: hvo/dashboards/hvo-weather.yaml")
            .And.Contain("url: /local/hvo/hvo-weather-wind-card.js?v=1")
            .And.NotContain("http://")
            .And.NotContain("https://");
        operations.Should().Contain("navigation_path: /hvo-weather/overview")
            .And.NotContain("title: Wind And Rain");
    }

    [TestMethod]
    public void WeatherDashboard_CoversOperationalConditionsAndRequiredTrends()
    {
        var dashboard = File.ReadAllText(Path.Combine(ConfigurationRoot, "dashboards", "hvo-weather.yaml"));

        dashboard.Should().Contain("type: custom:hvo-weather-wind-card")
            .And.Contain("freshness_entity: sensor.hvo_davis_source_freshness")
            .And.Contain("freshness == 'live'")
            .And.Contain("freshness == 'stale'")
            .And.Contain("freshness == 'error'")
            .And.Contain("freshness in ['waiting', 'unknown']")
            .And.Contain("Local weather unavailable")
            .And.Contain("Last temperature update:")
            .And.Contain("sensor.davis_heat_index")
            .And.Contain("sensor.davis_wind_chill")
            .And.Contain("sensor.davis_rain_15_min")
            .And.Contain("condition: numeric_state")
            .And.Contain("above: -0.001")
            .And.Contain("sensor.davis_yearly_rain")
            .And.Contain("sensor.davis_moon_illumination")
            .And.Contain("sensor.davis_moonrise")
            .And.Contain("sensor.davis_moonset")
            .And.Contain("entity: weather.forecast_home")
            .And.Contain("name: External Cloud Coverage")
            .And.NotContain("Met.no")
            .And.Contain("Temperature And Dew Point - 24 Hours")
            .And.Contain("Barometric Pressure - 24 Hours")
            .And.Contain("Wind And Gust - 24 Hours")
            .And.Contain("Rain Accumulation - 7 Days")
            .And.Contain("Solar Radiation - 24 Hours")
            .And.Contain("UV Index - 24 Hours");
    }

    [TestMethod]
    public void ForecastCloudHelper_PreservesProviderAvailability()
    {
        var templates = File.ReadAllText(Path.Combine(ConfigurationRoot, "templates", "hvo.yaml"));

        templates.Should().Contain("HVO Forecast Cloud Coverage")
            .And.Contain("'weather.forecast_home' | has_value")
            .And.Contain("state_attr('weather.forecast_home', 'cloud_coverage') is not none");
    }

    [TestMethod]
    public void RequiredAstronomyEntities_AreNotMaskedByManagedEntityAllowlist()
    {
        var managed = File.ReadAllText(Path.Combine(ConfigurationRoot, "managed-entities.txt"));

        managed.Should().NotContain("sensor.davis_moon_phase")
            .And.NotContain("sensor.davis_moon_illumination")
            .And.NotContain("sensor.davis_moonrise")
            .And.NotContain("sensor.davis_moonset")
            .And.Contain("sensor.davis_rain_15_min")
            .And.Contain("sensor.davis_rain_1_hour")
            .And.Contain("sensor.davis_wind_speed_2_min_average");
    }

    [TestMethod]
    public void WeatherFrontendResource_IsRegisteredCopiedVerifiedAndRollbackSafe()
    {
        var script = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "HomeAssistant",
            "deploy-home-assistant-dashboard.sh"));
        var frontend = File.ReadAllText(Path.Combine(ConfigurationRoot, "frontend", "hvo-weather-wind-card.js"));

        script.Should().Contain("deploy_file \"$frontend_root/hvo-weather-wind-card.js\" \"$staging_root/frontend/hvo-weather-wind-card.js\"")
            .And.Contain("mv '$guest_config_root/hvo/frontend' '$guest_config_root/www/hvo'")
            .And.Contain("test -s '$guest_config_root/www/hvo/hvo-weather-wind-card.js'")
            .And.Contain("cp -a '$guest_config_root/www/hvo' '$backup_root/www-hvo'")
            .And.Contain("cp -a '$backup_root/www-hvo' '$guest_config_root/www/hvo'")
            .And.Contain("weather.forecast_home must expose numeric cloud_coverage");
        frontend.Should().Contain("customElements.define(\"hvo-weather-wind-card\"")
            .And.NotContain("http://")
            .And.NotContain("https://");
    }
}
