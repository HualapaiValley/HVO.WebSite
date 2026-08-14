using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.WeatherUnderground;
using Microsoft.AspNetCore.WebUtilities;

namespace HVO.Hardware.DavisVantagePro2.Tests.WeatherUnderground;

[TestClass]
public sealed class WeatherUndergroundQueryBuilderTests
{
    [TestMethod]
    public void BuildRelativeUri_MapsAvailableDavisMeasurementsAndRapidFireFields()
    {
        var observation = new Loop2Packet
        {
            RecordedAtUtc = new DateTime(2026, 8, 14, 12, 34, 56, DateTimeKind.Utc),
            OutsideTemperatureF = 81.25,
            InsideTemperatureF = 72.5,
            DewPointF = 44.1,
            OutsideHumidityPercent = 23,
            InsideHumidityPercent = 35,
            WindSpeedMph = 12.5,
            WindDirectionDegrees = 271,
            WindSpeed2MinAvgMph = 10.2,
            WindGust10MinMph = 19.8,
            WindGust10MinDirectionDegrees = 280,
            HourRainInches = 0.12,
            DailyRainInches = 0.34,
            BarometricPressureInHg = 29.92,
            SolarRadiationWm2 = 845,
            UvIndex = 7.2,
        };

        var uri = WeatherUndergroundQueryBuilder.BuildRelativeUri(
            "KAZKINGM12",
            "synthetic &?+/= key",
            observation,
            5);
        var query = QueryHelpers.ParseQuery(uri.OriginalString);

        query["ID"].ToString().Should().Be("KAZKINGM12");
        query["PASSWORD"].ToString().Should().Be("synthetic &?+/= key");
        query["dateutc"].ToString().Should().Be("2026-08-14 12:34:56");
        query["tempf"].ToString().Should().Be("81.25");
        query["indoortempf"].ToString().Should().Be("72.5");
        query["dewptf"].ToString().Should().Be("44.1");
        query["humidity"].ToString().Should().Be("23");
        query["indoorhumidity"].ToString().Should().Be("35");
        query["windspeedmph"].ToString().Should().Be("12.5");
        query["winddir"].ToString().Should().Be("271");
        query["windspdmph_avg2m"].ToString().Should().Be("10.2");
        query["windgustmph_10m"].ToString().Should().Be("19.8");
        query["windgustdir_10m"].ToString().Should().Be("280");
        query["rainin"].ToString().Should().Be("0.12");
        query["dailyrainin"].ToString().Should().Be("0.34");
        query["baromin"].ToString().Should().Be("29.92");
        query["solarradiation"].ToString().Should().Be("845");
        query["UV"].ToString().Should().Be("7.2");
        query["action"].ToString().Should().Be("updateraw");
        query["realtime"].ToString().Should().Be("1");
        query["rtfreq"].ToString().Should().Be("5");
        uri.OriginalString.Should().Contain("PASSWORD=synthetic%20%26%3F%2B%2F%3D%20key");
        query.Keys.Should().NotContain(key => key.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void BuildRelativeUri_OmitsUnavailableAndNonFiniteOptionalMeasurements()
    {
        var uri = WeatherUndergroundQueryBuilder.BuildRelativeUri(
            "KAZKINGM12",
            "synthetic-key",
            new Loop2Packet
            {
                RecordedAtUtc = new DateTime(2026, 8, 14, 12, 34, 56, DateTimeKind.Utc),
                OutsideTemperatureF = null,
                WindSpeedMph = double.NaN,
                UvIndex = double.PositiveInfinity,
            },
            5);
        var query = QueryHelpers.ParseQuery(uri.OriginalString);

        query.Keys.Should().NotContain(["tempf", "windspeedmph", "UV"]);
        query.Keys.Should().Contain(["ID", "PASSWORD", "dateutc", "action", "realtime", "rtfreq"]);
    }

    [TestMethod]
    public void Endpoint_UsesOfficialRapidFireHttpsEndpoint()
    {
        WeatherUndergroundQueryBuilder.Endpoint.Scheme.Should().Be(Uri.UriSchemeHttps);
        WeatherUndergroundQueryBuilder.Endpoint.Host.Should().Be("rtupdate.wunderground.com");
        WeatherUndergroundQueryBuilder.Endpoint.AbsolutePath.Should().Be("/weatherstation/updateweatherstation.php");
    }
}
