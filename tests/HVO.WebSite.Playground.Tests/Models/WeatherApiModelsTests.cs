using FluentAssertions;
using HVO.DataModels.Models;
using HVO.DataModels.RawModels;
using HVO.WebSite.Playground.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.WebSite.Playground.Tests.Models;

/// <summary>
/// Unit tests for Weather API Models
/// </summary>
[TestClass]public class WeatherApiModelsTests
{
    #region LatestWeatherResponse Tests

    [TestMethod]
    public void LatestWeatherResponse_ShouldInitializeWithDefaultValues()
    {
        // Act
        var response = new LatestWeatherResponse();

        // Assert
        response.Timestamp.Should().Be(default(DateTime));
        response.MachineName.Should().Be(string.Empty);
        response.Data.Should().NotBeNull();
        response.Data.Should().BeOfType<DavisVantageProConsoleRecordsNew>();
    }

    [TestMethod]
    public void LatestWeatherResponse_ShouldAllowSettingProperties()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var machineName = "TestMachine";
        var weatherData = new DavisVantageProConsoleRecordsNew 
        { 
            Id = 123,
            RecordDateTime = DateTimeOffset.UtcNow,
            OutsideTemperature = 75.5m
        };

        // Act
        var response = new LatestWeatherResponse
        {
            Timestamp = timestamp,
            MachineName = machineName,
            Data = weatherData
        };

        // Assert
        response.Timestamp.Should().Be(timestamp);
        response.MachineName.Should().Be(machineName);
        response.Data.Should().Be(weatherData);
        response.Data.Id.Should().Be(123);
        response.Data.OutsideTemperature.Should().Be(75.5m);
    }

    #endregion

    #region WeatherHighsLowsResponse Tests

    [TestMethod]
    public void WeatherHighsLowsResponse_ShouldInitializeWithDefaultValues()
    {
        // Act
        var response = new WeatherHighsLowsResponse();

        // Assert
        response.Timestamp.Should().Be(default(DateTime));
        response.MachineName.Should().Be(string.Empty);
        response.DateRange.Should().NotBeNull();
        response.DateRange.Should().BeOfType<DateRangeInfo>();
        response.Data.Should().NotBeNull();
        response.Data.Should().BeOfType<WeatherRecordHighLowSummary>();
    }

    [TestMethod]
    public void WeatherHighsLowsResponse_ShouldAllowSettingProperties()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var machineName = "TestMachine";
        var dateRange = new DateRangeInfo 
        { 
            Start = DateTimeOffset.UtcNow.Date.AddDays(-7),
            End = DateTimeOffset.UtcNow.Date
        };
        var summary = new WeatherRecordHighLowSummary
        {
            OutsideTemperatureHigh = 85.0m,
            OutsideTemperatureLow = 55.0m
        };

        // Act
        var response = new WeatherHighsLowsResponse
        {
            Timestamp = timestamp,
            MachineName = machineName,
            DateRange = dateRange,
            Data = summary
        };

        // Assert
        response.Timestamp.Should().Be(timestamp);
        response.MachineName.Should().Be(machineName);
        response.DateRange.Should().Be(dateRange);
        response.Data.Should().Be(summary);
        response.Data.OutsideTemperatureHigh.Should().Be(85.0m);
        response.Data.OutsideTemperatureLow.Should().Be(55.0m);
    }

    #endregion

    #region Property Validation Tests

    [TestMethod]
    public void LatestWeatherResponse_Properties_ShouldBeGettableAndSettable()
    {
        // Arrange
        var response = new LatestWeatherResponse();
        var testTime = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var testMachine = "Observatory-Main";
        var testData = new DavisVantageProConsoleRecordsNew();

        // Act & Assert - Test that all properties can be set and retrieved
        response.Timestamp = testTime;
        response.Timestamp.Should().Be(testTime);

        response.MachineName = testMachine;
        response.MachineName.Should().Be(testMachine);

        response.Data = testData;
        response.Data.Should().Be(testData);
    }

    [TestMethod]
    public void WeatherHighsLowsResponse_Properties_ShouldBeGettableAndSettable()
    {
        // Arrange
        var response = new WeatherHighsLowsResponse();
        var testTime = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var testMachine = "Observatory-Main";
        var testDateRange = new DateRangeInfo();
        var testData = new WeatherRecordHighLowSummary();

        // Act & Assert - Test that all properties can be set and retrieved
        response.Timestamp = testTime;
        response.Timestamp.Should().Be(testTime);

        response.MachineName = testMachine;
        response.MachineName.Should().Be(testMachine);

        response.DateRange = testDateRange;
        response.DateRange.Should().Be(testDateRange);

        response.Data = testData;
        response.Data.Should().Be(testData);
    }

    #endregion

    #region Edge Cases and Validation

    [TestMethod]
    public void LatestWeatherResponse_WithNullMachineName_ShouldHandleGracefully()
    {
        // Arrange & Act
        var response = new LatestWeatherResponse
        {
            MachineName = null!
        };

        // Assert
        response.MachineName.Should().BeNull();
    }

    [TestMethod]
    public void WeatherHighsLowsResponse_WithNullMachineName_ShouldHandleGracefully()
    {
        // Arrange & Act
        var response = new WeatherHighsLowsResponse
        {
            MachineName = null!
        };

        // Assert
        response.MachineName.Should().BeNull();
    }

    [TestMethod]
    public void LatestWeatherResponse_WithComplexWeatherData_ShouldMaintainDataIntegrity()
    {
        // Arrange
        var complexWeatherData = new DavisVantageProConsoleRecordsNew
        {
            Id = 999,
            RecordDateTime = DateTimeOffset.UtcNow,
            OutsideTemperature = 72.3m,
            OutsideHumidity = 65,
            Barometer = 30.15m,
            WindSpeed = 8,
            WindDirection = 225,
            RainRate = 0.25m,
            UvIndex = 7
        };

        // Act
        var response = new LatestWeatherResponse
        {
            Data = complexWeatherData
        };

        // Assert
        response.Data.Should().BeEquivalentTo(complexWeatherData);
        response.Data.Id.Should().Be(999);
        response.Data.OutsideTemperature.Should().Be(72.3m);
        response.Data.WindDirection.Should().Be(225);
    }

    #endregion
}
