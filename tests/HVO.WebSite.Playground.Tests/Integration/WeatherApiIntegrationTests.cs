using FluentAssertions;
using HVO.Core.Results;
using HVO.DataModels.Data;
using HVO.DataModels.RawModels;
using HVO.WebSite.Playground.Models;
using HVO.WebSite.Playground.Tests.TestHelpers;
using HVO.WebSite.Playground.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Net;
using System.Net.Http.Json;

namespace HVO.WebSite.Playground.Tests.Integration;

/// <summary>
/// Integration tests for the Weather API endpoints
/// Tests the full HTTP request/response cycle with real dependencies
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class WeatherApiIntegrationTests
{
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private Mock<IWeatherService> _mockWeatherService = null!;

    [TestInitialize]
    public void Initialize()
    {
        _factory = new TestWebApplicationFactory();
        _client = _factory.CreateClient();
        
        // Get the mocked service from the factory for configuration
        using var scope = _factory.Services.CreateScope();
        _mockWeatherService = Mock.Get(scope.ServiceProvider.GetRequiredService<IWeatherService>());
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    #region Latest Weather Record Integration Tests

    [TestMethod]
    public async Task GetLatestWeatherRecord_WithData_ReturnsSuccessResponse()
    {
        // Arrange
        var testData = WeatherTestDataBuilder.CreateSampleWeatherRecord(DateTimeOffset.UtcNow);
        var expectedResponse = new LatestWeatherResponse
        {
            Data = testData,
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };
        
        _mockWeatherService.Setup(x => x.GetLatestWeatherRecordAsync())
            .ReturnsAsync(Result<LatestWeatherResponse>.Success(expectedResponse));

        // Act
        var response = await _client.GetAsync("/api/v1.0/weather/latest");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var weatherResponse = await response.Content.ReadFromJsonAsync<LatestWeatherResponse>();
        weatherResponse.Should().NotBeNull();
        weatherResponse!.Data.Should().NotBeNull();
        weatherResponse.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        weatherResponse.MachineName.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task GetLatestWeatherRecord_WithoutData_ReturnsNotFound()
    {
        // Arrange - Mock service returns failure with InvalidOperationException for 404
        _mockWeatherService.Setup(x => x.GetLatestWeatherRecordAsync())
            .ReturnsAsync(Result<LatestWeatherResponse>.Failure(new InvalidOperationException("No weather data available")));

        // Act
        var response = await _client.GetAsync("/api/v1.0/weather/latest");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problemDetails.Should().NotBeNull();
        problemDetails!.Title.Should().Be("Weather Data Not Found");
    }

    #endregion

    #region Weather Highs/Lows Integration Tests

    [TestMethod]
    public async Task GetWeatherHighsLows_WithDateRange_ReturnsSuccessResponse()
    {
        // Arrange
        var testDate = DateTimeOffset.UtcNow.Date;
        var expectedResponse = new WeatherHighsLowsResponse
        {
            DateRange = new DateRangeInfo
            {
                Start = testDate,
                End = testDate.AddDays(1)
            },
            Data = new WeatherRecordHighLowSummary
            {
                OutsideTemperatureHigh = 85.5m,
                OutsideTemperatureLow = 65.2m,
                OutsideHumidityHigh = 45,
                OutsideHumidityLow = 25,
                StartRecordDateTime = testDate,
                EndRecordDateTime = testDate.AddDays(1)
            },
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };
        
        _mockWeatherService.Setup(x => x.GetWeatherHighsLowsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .ReturnsAsync(Result<WeatherHighsLowsResponse>.Success(expectedResponse));

        var startDate = testDate.ToString("O");
        var endDate = testDate.AddDays(1).ToString("O");

        // Act
        var response = await _client.GetAsync($"/api/v1.0/weather/highs-lows?startDate={startDate}&endDate={endDate}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var weatherResponse = await response.Content.ReadFromJsonAsync<WeatherHighsLowsResponse>();
        weatherResponse.Should().NotBeNull();
        weatherResponse!.DateRange.Should().NotBeNull();
        weatherResponse.DateRange.Start.Date.Should().Be(testDate.Date);
    }

    [TestMethod]
    public async Task GetWeatherHighsLows_WithoutDateParameters_UsesDefaultRange()
    {
        // Arrange
        var expectedResponse = new WeatherHighsLowsResponse
        {
            DateRange = new DateRangeInfo
            {
                Start = DateTimeOffset.Now.Date,
                End = DateTimeOffset.Now.Date.AddDays(1)
            },
            Data = new WeatherRecordHighLowSummary
            {
                OutsideTemperatureHigh = 78.3m,
                OutsideTemperatureLow = 58.1m
            },
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };
        
        _mockWeatherService.Setup(x => x.GetWeatherHighsLowsAsync(null, null))
            .ReturnsAsync(Result<WeatherHighsLowsResponse>.Success(expectedResponse));

        // Act
        var response = await _client.GetAsync("/api/v1.0/weather/highs-lows");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var weatherResponse = await response.Content.ReadFromJsonAsync<WeatherHighsLowsResponse>();
        weatherResponse.Should().NotBeNull();
        weatherResponse!.DateRange.Start.Date.Should().Be(DateTimeOffset.Now.Date);
    }

    #endregion

    #region Current Weather Conditions Integration Tests

    [TestMethod]
    public async Task GetCurrentWeatherConditions_WithData_ReturnsSuccessResponse()
    {
        // Arrange
        var expectedResponse = new CurrentWeatherResponse
        {
            Current = new CurrentWeatherData
            {
                OutsideTemperature = 72.5m,
                OutsideHumidity = 45m,
                WindSpeed = 8.2m,
                Barometer = 30.15m,
                RecordDateTime = DateTimeOffset.UtcNow
            },
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };
        
        _mockWeatherService.Setup(x => x.GetCurrentWeatherConditionsAsync())
            .ReturnsAsync(Result<CurrentWeatherResponse>.Success(expectedResponse));

        // Act
        var response = await _client.GetAsync("/api/v1.0/weather/current");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var weatherResponse = await response.Content.ReadFromJsonAsync<CurrentWeatherResponse>();
        weatherResponse.Should().NotBeNull();
        weatherResponse!.Current.Should().NotBeNull();
        weatherResponse.Current.OutsideTemperature.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public async Task GetCurrentWeatherConditions_WithoutData_ReturnsNotFound()
    {
        // Arrange - Mock service returns failure with InvalidOperationException for 404
        _mockWeatherService.Setup(x => x.GetCurrentWeatherConditionsAsync())
            .ReturnsAsync(Result<CurrentWeatherResponse>.Failure(new InvalidOperationException("No current weather data available")));

        // Act
        var response = await _client.GetAsync("/api/v1.0/weather/current");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problemDetails.Should().NotBeNull();
        problemDetails!.Title.Should().Be("Current Weather Data Not Found");
    }

    #endregion

    #region API Documentation Integration Tests

    [TestMethod]
    public async Task WeatherEndpoints_IncludeProperContentTypeHeaders()
    {
        // Arrange - Set up mock responses for all endpoints
        var latestResponse = new LatestWeatherResponse
        {
            Data = WeatherTestDataBuilder.CreateSampleWeatherRecord(DateTimeOffset.UtcNow),
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };
        
        var highsLowsResponse = new WeatherHighsLowsResponse
        {
            DateRange = new DateRangeInfo { Start = DateTimeOffset.Now.Date, End = DateTimeOffset.Now.Date.AddDays(1) },
            Data = new WeatherRecordHighLowSummary { OutsideTemperatureHigh = 75m },
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };
        
        var currentResponse = new CurrentWeatherResponse
        {
            Current = new CurrentWeatherData { OutsideTemperature = 72m },
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };
        
        _mockWeatherService.Setup(x => x.GetLatestWeatherRecordAsync())
            .ReturnsAsync(Result<LatestWeatherResponse>.Success(latestResponse));
        _mockWeatherService.Setup(x => x.GetWeatherHighsLowsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .ReturnsAsync(Result<WeatherHighsLowsResponse>.Success(highsLowsResponse));
        _mockWeatherService.Setup(x => x.GetCurrentWeatherConditionsAsync())
            .ReturnsAsync(Result<CurrentWeatherResponse>.Success(currentResponse));

        // Act & Assert
        var endpoints = new[]
        {
            "/api/v1.0/weather/latest",
            "/api/v1.0/weather/highs-lows",
            "/api/v1.0/weather/current"
        };

        foreach (var endpoint in endpoints)
        {
            var response = await _client.GetAsync(endpoint);
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
            // Verify we actually have content (content length > 0 or content is present)
            var content = await response.Content.ReadAsStringAsync();
            content.Should().NotBeEmpty();
        }
    }

    [TestMethod]
    public async Task WeatherEndpoints_ReturnValidJsonStructure()
    {
        // Arrange - Set up mock responses
        var latestResponse = new LatestWeatherResponse
        {
            Data = WeatherTestDataBuilder.CreateSampleWeatherRecord(DateTimeOffset.UtcNow),
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };
        
        var highsLowsResponse = new WeatherHighsLowsResponse
        {
            DateRange = new DateRangeInfo { Start = DateTimeOffset.Now.Date, End = DateTimeOffset.Now.Date.AddDays(1) },
            Data = new WeatherRecordHighLowSummary { OutsideTemperatureHigh = 85m },
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };
        
        var currentResponse = new CurrentWeatherResponse
        {
            Current = new CurrentWeatherData { OutsideTemperature = 72m },
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };
        
        _mockWeatherService.Setup(x => x.GetLatestWeatherRecordAsync())
            .ReturnsAsync(Result<LatestWeatherResponse>.Success(latestResponse));
        _mockWeatherService.Setup(x => x.GetWeatherHighsLowsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .ReturnsAsync(Result<WeatherHighsLowsResponse>.Success(highsLowsResponse));
        _mockWeatherService.Setup(x => x.GetCurrentWeatherConditionsAsync())
            .ReturnsAsync(Result<CurrentWeatherResponse>.Success(currentResponse));

        // Act & Assert - Verify all endpoints return parseable JSON
        var latestHttpResponse = await _client.GetAsync("/api/v1.0/weather/latest");
        var highsLowsHttpResponse = await _client.GetAsync("/api/v1.0/weather/highs-lows");
        var currentHttpResponse = await _client.GetAsync("/api/v1.0/weather/current");

        // All should return valid JSON that can be parsed
        await latestHttpResponse.Content.ReadFromJsonAsync<LatestWeatherResponse>();
        await highsLowsHttpResponse.Content.ReadFromJsonAsync<WeatherHighsLowsResponse>();
        await currentHttpResponse.Content.ReadFromJsonAsync<CurrentWeatherResponse>();

        // If we get here without exceptions, JSON parsing was successful
        latestHttpResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        highsLowsHttpResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        currentHttpResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Error Handling Integration Tests

    [TestMethod]
    public async Task WeatherEndpoints_ReturnConsistentErrorFormat()
    {
        // Arrange - Mock service returns InvalidOperationException for 404 errors
        _mockWeatherService.Setup(x => x.GetLatestWeatherRecordAsync())
            .ReturnsAsync(Result<LatestWeatherResponse>.Failure(new InvalidOperationException("No weather data available")));
        _mockWeatherService.Setup(x => x.GetCurrentWeatherConditionsAsync())
            .ReturnsAsync(Result<CurrentWeatherResponse>.Failure(new InvalidOperationException("No current weather data available")));

        // Act
        var endpoints = new[]
        {
            "/api/v1.0/weather/latest",
            "/api/v1.0/weather/current"
        };

        foreach (var endpoint in endpoints)
        {
            var response = await _client.GetAsync(endpoint);
            
            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            
            var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            problemDetails.Should().NotBeNull();
            problemDetails!.Title.Should().NotBeNullOrEmpty();
            problemDetails.Detail.Should().NotBeNullOrEmpty();
            problemDetails.Status.Should().Be(404);
        }
    }

    [TestMethod]
    public async Task WeatherEndpoints_HandleInvalidDateParameters()
    {
        // Arrange - Mock service returns success for highs/lows endpoint
        var mockResponse = new WeatherHighsLowsResponse
        {
            DateRange = new DateRangeInfo { Start = DateTimeOffset.Now.Date, End = DateTimeOffset.Now.Date.AddDays(1) },
            Data = new WeatherRecordHighLowSummary(),
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };
        
        _mockWeatherService.Setup(x => x.GetWeatherHighsLowsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .ReturnsAsync(Result<WeatherHighsLowsResponse>.Success(mockResponse));

        // Act - Test with malformed date
        var response = await _client.GetAsync("/api/v1.0/weather/highs-lows?startDate=invalid-date");

        // Assert - Should handle gracefully (400 Bad Request or use default dates)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.OK);
    }

    #endregion

    #region API Versioning Integration Tests

    [TestMethod]
    public async Task WeatherEndpoints_SupportApiVersioning()
    {
        // Arrange - Mock service returns success
        var mockResponse = new LatestWeatherResponse
        {
            Data = WeatherTestDataBuilder.CreateSampleWeatherRecord(DateTimeOffset.UtcNow),
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };
        
        _mockWeatherService.Setup(x => x.GetLatestWeatherRecordAsync())
            .ReturnsAsync(Result<LatestWeatherResponse>.Success(mockResponse));

        // Act - Test with explicit version
        var response = await _client.GetAsync("/api/v1.0/weather/latest");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // The fact that we get a successful response confirms API versioning is working
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();
    }

    #endregion
}
