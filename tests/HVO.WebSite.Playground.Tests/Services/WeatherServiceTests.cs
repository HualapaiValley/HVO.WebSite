using FluentAssertions;
using HVO.Core.Results;
using HVO.DataModels.Data;
using HVO.DataModels.Models;
using HVO.WebSite.Playground.Services;
using HVO.WebSite.Playground.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.WebSite.Playground.Tests.Services;

/// <summary>
/// Unit tests for the WeatherService class
/// Tests business logic independently from HTTP concerns
/// </summary>
[TestClass]public class WeatherServiceTests : IDisposable
{
    private readonly HvoDbContext _context;
    private readonly Mock<ILogger<WeatherService>> _mockLogger;
    private readonly WeatherService _weatherService;

    public WeatherServiceTests()
    {
        // Setup in-memory database for testing using factory
        _context = TestDbContextFactory.CreateInMemoryContext($"WeatherServiceTests-{Guid.NewGuid()}");
        _mockLogger = new Mock<ILogger<WeatherService>>();
        _weatherService = new WeatherService(_context, _mockLogger.Object);
    }

    #region Latest Weather Record Tests

    [TestMethod]
    public async Task GetLatestWeatherRecordAsync_WithValidData_ReturnsSuccessResult()
    {
        // Arrange
        var testRecord = WeatherTestDataBuilder.CreateSampleWeatherRecord();
        _context.DavisVantageProConsoleRecordsNews.Add(testRecord);
        await _context.SaveChangesAsync();

        // Act
        var result = await _weatherService.GetLatestWeatherRecordAsync();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Data.Id.Should().Be(testRecord.Id);
    }

    [TestMethod]
    public async Task GetLatestWeatherRecordAsync_WithNoData_ReturnsFailureResult()
    {
        // Arrange - empty database

        // Act
        var result = await _weatherService.GetLatestWeatherRecordAsync();

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Message.Should().Contain("No weather records found");
    }

    [TestMethod]
    public async Task GetLatestWeatherRecordAsync_WithMultipleRecords_ReturnsLatestRecord()
    {
        // Arrange
        var oldRecord = WeatherTestDataBuilder.CreateSampleWeatherRecord(DateTimeOffset.UtcNow.AddHours(-2));
        var latestRecord = WeatherTestDataBuilder.CreateSampleWeatherRecord(DateTimeOffset.UtcNow.AddHours(-1));
        
        // Set different IDs to avoid conflicts
        oldRecord.Id = 1;
        latestRecord.Id = 2;
        
        _context.DavisVantageProConsoleRecordsNews.Add(oldRecord);
        _context.DavisVantageProConsoleRecordsNews.Add(latestRecord);
        await _context.SaveChangesAsync();

        // Act
        var result = await _weatherService.GetLatestWeatherRecordAsync();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Data.Id.Should().Be(latestRecord.Id);
        result.Value.Data.RecordDateTime.Should().Be(latestRecord.RecordDateTime);
    }

    #endregion

    #region Weather Highs/Lows Tests

    [TestMethod]
    public async Task GetWeatherHighsLowsAsync_WithValidDateRange_ReturnsExpectedFailure()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.Date;
        var endDate = startDate.AddDays(1);

        // Act - This should fail with in-memory database because it uses stored procedures
        var result = await _weatherService.GetWeatherHighsLowsAsync(startDate, endDate);

        // Assert - In-memory database doesn't support stored procedures, so we expect failure
        result.IsSuccessful.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Message.Should().Contain("FromSqlQueryRootExpression");
    }

    #endregion

    #region Current Weather Conditions Tests

    [TestMethod]
    public async Task GetCurrentWeatherConditionsAsync_WithValidData_ReturnsExpectedFailure()
    {
        // Arrange
        var testRecord = WeatherTestDataBuilder.CreateSampleWeatherRecord();
        _context.DavisVantageProConsoleRecordsNews.Add(testRecord);
        await _context.SaveChangesAsync();

        // Act - This should fail with in-memory database because it uses stored procedures for extremes
        var result = await _weatherService.GetCurrentWeatherConditionsAsync();

        // Assert - In-memory database doesn't support stored procedures, so we expect failure
        result.IsSuccessful.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Message.Should().Contain("FromSqlQueryRootExpression");
    }

    [TestMethod]
    public async Task GetCurrentWeatherConditionsAsync_WithNoCurrentRecord_ReturnsFailureResult()
    {
        // Arrange - Start with empty database
        // No records added

        // Act
        var result = await _weatherService.GetCurrentWeatherConditionsAsync();

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Message.Should().Contain("No current weather data is available");
    }

    #endregion

    #region Dispose Pattern

    public void Dispose()
    {
        _context.Dispose();
    }

    #endregion
}
